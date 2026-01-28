using Microsoft.Win32;
using Newtonsoft.Json;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using wpfCCTV.Models;
namespace wpfCCTV
{
    /// <summary>
    /// MainWindow.xaml에 대한 상호 작용 논리
    /// </summary>
    public partial class MainWindow : System.Windows.Window
    {
        private ModelManager Manager;  // ⭐ 멀티 모델 관리자
        private VideoCapture Capture;
        private CancellationTokenSource CancellationTokenSource;
        private Mat CurrentFrame;
        private Mat CurrentDetectionFrame;
        private readonly Random Randoms = new Random();
        private readonly Dictionary<int, Color> ClassColor= new Dictionary<int, Color>();

        // 비디오 프로그레스 바 관련
        private int TotalFrames = 0;
        private int CurrentFrameNumber = 0;
        private double VideoFps = 30;

        // 자동 저장 관련
        private bool AutoSaveEnabled = false;
        private string AutoSavePath = "";
        private int AutoSaveCounter = 0;

        // 조건부 저장 (트리거) 관련
        private bool TriggerSaveEnabled = false;
        private HashSet<string> TriggerClasses = new HashSet<string>();
        private string TriggerSavePath = "";

        // 통계
        private int TotalDetectedObjects = 0;
        private Dictionary<string, int> ClassCountTotal = new Dictionary<string, int>();

        public MainWindow()
        {
            InitializeYoloModel();
            InitializeComponent();
        }
        /// <summary>
        /// 초기화
        /// </summary>
        private async void InitializeYoloModel()
        {
            try
            {
                StatusText.Text = "YOLO 모델 로딩 중...";
                await Task.Run(() =>
                {
                    // 모델 관리자 생성
                    Manager = new ModelManager();
                    // 객체 감지 모델 로드
                    var objectsettings = YoloSettings.CreateObjectDetectionSettings();
                    Manager.LoadModel(objectsettings);
                    // 얼굴 인식 모델 로드
                    try
                    { 
                         var facesettings = YoloSettings.CreateFaceDetectionSettings();
                        Manager.LoadModel(facesettings);
                        Dispatcher.Invoke(() =>
                        {
                            FaceDetectionRadio.IsEnabled = true;
                            StatusText.Text = "모델 로딩 완료: 객체 감지 및 얼굴 인식 모델 모두 로드됨.";
                        });
                    }
                    catch (Exception ex)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            FaceDetectionRadio.IsEnabled = false;
                            FaceDetectionRadio.ToolTip = "모델 파일 없음";
                            Log($"얼굴 인식 모델 없음 : {ex.Message}");
                        });
                    }

                });
                StatusText.Text = "✅ 준비 완료! 소스를 선택하세요.";
                Log("객체 감지 모델 로드 성공");
                EnableControls(true);
            }
            catch (Exception ex)
            { 
                StatusText.Text = $"모델 로딩 실패: {ex.Message}";
                MessageBox.Show($"YOLO 모델 로딩 중 오류가 발생했습니다: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                Log($"❌ 모델 로딩 실패: {ex.Message}");
            }
        }
        private void EnableControls(bool enabled)
        {
            LoadImageButton.IsEnabled = enabled;
            StartWebcamButton.IsEnabled = enabled;
            LoadVideoButton.IsEnabled = enabled;
        }
        /// <summary>
        /// 임계값 조정
        /// </summary>
        private void ConfidenceSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (ConfidenceText != null)
            {
                ConfidenceText.Text = e.NewValue.ToString("F1");
            }

            // ⭐ 활성 모델에 실시간 반영
            if (Manager?.ActiveModel != null)
            {
                try
                {
                    Manager.ActiveModel.SetConfidenceThreshold((float)e.NewValue);
                    // Log($"🎚️ 신뢰도 임계값 변경: {e.NewValue:F1}"); // 로그 너무 많아질 수 있음
                }
                catch (Exception ex)
                {
                    Log($"임계값 변경 오류: {ex.Message}");
                }
            }
        }
        /// <summary>
        /// 자동 저장 체크 박스 
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void AutoSaveCheckBox_Changed(object sender, RoutedEventArgs e)
        { 
             AutoSaveEnabled = AutoSaveCheckBox.IsChecked ?? false;

            if (AutoSaveEnabled && string.IsNullOrEmpty(AutoSavePath))
            { 
                MessageBox.Show("자동 저장 경로를 설정해주세요.", "경고", MessageBoxButton.OK, MessageBoxImage.Warning);
                AutoSaveCheckBox.IsChecked = false;
                AutoSaveEnabled =false;
            }
            else if (AutoSaveEnabled)
            {
                Log($" 자동 저장 활성화: {AutoSavePath}");
            }
            else
            {
                Log(" 자동 저장 비활성화");
            }
        }
        /// <summary>
        /// 자동 저장 폴더 다이얼로그 박스 
        /// </summary>
        private void SelectAutoSaveFolderButton_Click(object sender, RoutedEventArgs e)
        {
            // 폴더 선택 다이얼로그
            var Dialog = new System.Windows.Forms.FolderBrowserDialog
            { 
                Description = "자동 저장 폴더 선택",
                ShowNewFolderButton = true
             };
            if (Dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            { 
                 AutoSavePath = Dialog.SelectedPath;
                AutoSaveFolderText.Text = AutoSavePath;
                AutoSaveFolderText.Foreground = new SolidColorBrush(Colors.Green);
                Log($" 자동 저장 폴더 설정: {AutoSavePath}");
            }

        }
        /// <summary>
        ///  로그 기록 초기화
        /// </summary>
        private void ClearLogButton_Click(object sender, RoutedEventArgs e)
        {
            LogTextBlock.Text = "";
        }
        /// <summary>
        /// 모델 전환 이벤트
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void ModelType_Changed(object sender, RoutedEventArgs e)
        {
            if (Manager == null)
                return;
            try
            {
                if (ObjectDetectionRadio.IsChecked == true)
                {
                    Manager.SwitchModel(YoloModelType.ObjectDetection);
                    CurrentModelText.Text = "객체 감지 모델 활성화";
                    CurrentModelText.Foreground = new SolidColorBrush(Colors.Blue);
                    Log("모델 전환: 객체 감지 모델 활성화");
                }
                else
                {
                    Manager.SwitchModel(YoloModelType.FaceDetection);
                    CurrentModelText.Text = "객체 감지 모델 활성화";
                    CurrentModelText.Foreground = new SolidColorBrush(Colors.Blue);
                    Log("모델 전환: 객체 감지 모델 활성화");
                }    
            }
            catch (Exception ex)
            { 
               MessageBox.Show($"모델 전환 중 오류가 발생했습니다: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        /// <summary>
        ///  이미지 파일 열기
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void LoadImageButton_Click(object sender, RoutedEventArgs e)
        {
            //파일 열기 다이얼로그
            var Dialog = new OpenFileDialog
            {
                Filter = "이미지 파일|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.tiff|모든 파일|*.*",
                Title = "이미지 선택"
            };
            if (Dialog.ShowDialog()==true)
            {
                try
                {
                    StatusText.Visibility = Visibility.Collapsed;
                    VideoProgressPanel.Visibility = Visibility.Collapsed;
                    // 이미지 읽는 함수
                    CurrentFrame =Cv2.ImRead(Dialog.FileName);
                    Log($"이미지 파일 로드: {Dialog.FileName}");
                    await DetectAndDisplayAsync(CurrentFrame);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"이미지 로딩 오류: {ex.Message}", "오류",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    Log($"❌ 이미지 로딩 오류: {ex.Message}");
                }
            }
        }
        /// <summary>
        /// 웹캠 키는 함수
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void StartWebcamButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                int cameraIndex = 0; // 기본 카메라 인덱스
                if (CameraComboBox.SelectedItem is ComboBoxItem selectedItem)
                {
                    cameraIndex = int.Parse(selectedItem.Tag.ToString() ?? "0");
                }
                Capture = new VideoCapture(cameraIndex);

                if (Capture.IsOpened())
                {
                    MessageBox.Show($"카메라 {cameraIndex}을(를) 열 수 없습니다.\n" +
                        "다른 프로그램에서 사용 중이거나 존재하지 않을 수 있습니다.",
                        "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                StartWebcamButton.IsEnabled = false;
                StopWebcamButton.IsEnabled = true;
                LoadImageButton.IsEnabled = false;
                LoadVideoButton.IsEnabled = false;
                CameraComboBox.IsEnabled = false;
                ObjectDetectionRadio.IsEnabled = false;
                FaceDetectionRadio.IsEnabled = false;
                StatusText.Visibility = Visibility.Collapsed;
                VideoProgressPanel.Visibility = Visibility.Collapsed;
                ConfidenceSlider.IsEnabled = false;
                CancellationTokenSource = new CancellationTokenSource();
                Log($"웹캠 {cameraIndex} 시작");
                await Task.Run(() => ProcessVideoStream(CancellationTokenSource.Token));
            }
            catch (Exception ex)
            {
                MessageBox.Show($"웹캠 시작 오류: {ex.Message}", "오류",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                Log($"❌ 웹캠 오류: {ex.Message}");
                ResetWebcamButtons();

            }
        }
        /// <summary>
        /// 웹캠 촬영 중단
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void StopWebcamButton_Click(object sender, RoutedEventArgs e)
        {
            StopVideoCapture();
        }
        /// <summary>
        /// 비디오 파일 열기
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void LoadVideoButton_Click(object sender, RoutedEventArgs e)
        {
            var openFileDialog = new OpenFileDialog
            {
                Filter = "비디오 파일|*.mp4;*.avi;*.mov;*.mkv;*.wmv;*.flv|모든 파일|*.*",
                Title = "비디오 선택"
            };
            if (openFileDialog.ShowDialog() == true)
            {
                try
                {
                    VideoProgressPanel.Visibility = Visibility.Visible;
                    // 비디오 캡처 초기화
                    Capture = new VideoCapture(openFileDialog.FileName);
                    if (!Capture.IsOpened())
                    {
                        MessageBox.Show("비디오 파일을 열 수 없습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    // 비디오 정보 가져오기
                    TotalFrames = (int)Capture.Get(VideoCaptureProperties.FrameCount);
                    VideoFps = Capture.Get(VideoCaptureProperties.Fps);
                    if(VideoFps<=0)
                        VideoFps = 30;
                    VideoProgressBar.Maximum = TotalFrames>0? TotalFrames:100;
                    VideoProgressBar.Value = 0;
                    CurrentFrameNumber = 0;
                    StartWebcamButton.IsEnabled = false;
                    StopWebcamButton.IsEnabled = true;
                    LoadImageButton.IsEnabled = false;
                    LoadVideoButton.IsEnabled = false;
                    CameraComboBox.IsEnabled = false;
                    ObjectDetectionRadio.IsEnabled = false;
                    FaceDetectionRadio.IsEnabled = false;
                    ConfidenceSlider.IsEnabled = false;
                    CancellationTokenSource = new CancellationTokenSource();
                    Log($"🎬 비디오 파일 로드: {openFileDialog.FileName}");
                    await Task.Run(() => ProcessVideoStream(CancellationTokenSource.Token));
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"비디오 로딩 오류: {ex.Message}", "오류",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    Log($"❌ 비디오 로딩 오류: {ex.Message}");
                }
            }
        }
        /// <summary>
        /// 비디오 스트림 처리
        /// </summary>
        /// <param name="token"></param>
        private void ProcessVideoStream(CancellationToken token)
        {
            while (!token.IsCancellationRequested && Capture != null)
            {
                try
                {
                    CurrentFrame = new Mat();
                    if (!Capture.Read(CurrentFrame) || CurrentFrame.Empty())
                    { 
                         Dispatcher.Invoke(() =>Log("비디오 종류"));
                        break;
                    }
                    CurrentFrameNumber++;

                    // 프로그래스바 업그레이드
                    Dispatcher.Invoke(() =>
                    {
                        if (VideoProgressPanel.Visibility == Visibility.Visible && TotalFrames > 0)
                        {
                            VideoProgressBar.Value = CurrentFrameNumber;
                            CurrentTimeText.Text = FormatTime(CurrentFrameNumber / VideoFps);
                        }
                    });
                    Dispatcher.Invoke(async () =>
                    {
                        await DetectAndDisplayAsync(CurrentFrame);
                    });
                    Thread.Sleep(33);
                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => Log($"❌ 프레임 처리 오류: {ex.Message}"));
                    break;
                }
            }
            Dispatcher.Invoke(StopVideoCapture);
        }
        private async Task DetectAndDisplayAsync(Mat frame)
        {
            // 활성 모델로 감지
            if (Manager?.ActiveModel == null || frame == null || frame.Empty())
                return;
            try
            { 
                 var start = DateTime.Now;
                // yolo감지
                var detections = await Task.Run(() => Manager.ActiveModel.Detect(frame));
                var elapsedMs = (DateTime.Now - start).TotalMilliseconds;
                // 감지 결과 그리기
                CurrentDetectionFrame = DrawDetections(frame.Clone(), detections);
                // wpf 컨트롤 에 표시
                DisplayImage.Source = CurrentDetectionFrame.ToBitmapSource();
                //통계업데이트 
                UpdateStatistics(detections,elapsedMs);
                // 자동 저장 처리
                if (AutoSaveEnabled && detections.Count > 0)
                {
                    AutoSaveDetection(detections);
                }
                // 트리거 저장 처리
                if (TriggerSaveEnabled && detections.Count > 0)
                {
                    TriggerSaveDetection(detections);
                }


            }
            catch (Exception ex)
            {
                Dispatcher.Invoke(() => Log($"❌ 감지 오류: {ex.Message}"));
            }
        }
        /// <summary>
        ///  토탈 통계 업데이트
        /// </summary>
        /// <param name="detections"></param>
        /// <param name="elapsedMs"></param>
        private void UpdateStatistics(List<Detection> detections, double elapsedMs)
        {
            // 총감지 객체수 업데이트
            TotalDetectedObjects += detections.Count;
            //클래스별 개수 집계
            var classCounts = detections
                .GroupBy(d => d.ClassName)
                .Select(x=>new {Class =x.Key, Count = x.Count()})
                .OrderByDescending(x=>x.Count).ToList();
            // 전체 통계 업데이트
            foreach (var item in classCounts)
            {
                if (ClassCountTotal.ContainsKey(item.Class))
                {
                    ClassCountTotal[item.Class] += item.Count;
                }
                else
                {
                    ClassCountTotal[item.Class]+= item.Count;
                }
            }
            //  현재 프레임 통계
            var stats = "현재 프레임\n"+$"감지 객체: {detections.Count}\n"+$"처리시간{elapsedMs:F0}\n"+$"fps : {(elapsedMs > 0 ? 1000 / elapsedMs : 0):F1}\\n\\n\" ";
            if (classCounts.Count > 0)
            {
                stats += "현재감지:\n";
                foreach (var item in classCounts)
                {
                    stats += $"{item.Class}: {item.Count}개\n";
                }
                stats += "\n";
            }
            //전체 세션 통계
            stats = "세션 전체\n" + $"감지 객체: {TotalDetectedObjects}\n" + $"자동저장 수:{AutoSaveCounter}장\n\n";
            if (ClassCountTotal.Count > 0)
            {
                stats += "누적 통계 :\n";
                foreach (var item in ClassCountTotal.OrderByDescending(x => x.Value).Take(10))
                {
                    stats += $"{item.Key}: {item.Value}개\n";
                }
            }

            StatsTextBlock.Text = stats;

            // 로그에 감지된 객체 기록
            if (detections.Count > 0)
            {
                var summary = string.Join(", ", detections
                    .GroupBy(d => d.ClassName)
                    .Select(g => $"{g.Key}×{g.Count()}"));
            }
        }
        /// <summary>
        ///  감지 결과 그리기
        /// </summary>
        /// <param name="frame"></param>
        /// <param name="detections"></param>
        /// <returns></returns>
        private Mat DrawDetections(Mat frame, List<Detection> detections)
        { 
            var ShowedLabel = ShowLabelsCheckBox.IsChecked ?? true;
            var ShowedConfidence = ShowConfidenceCheckBox.IsChecked ?? true;
            // 클래스별 고유색상 지정
            foreach (var detection in detections) 
            {
                if (ClassColor.ContainsKey(detection.ClassId))
                {
                    ClassColor[detection.ClassId] = System.Windows.Media.Color.FromRgb(
                        (byte)Randoms.Next(50, 255),
                        (byte)Randoms.Next(50, 255),
                        (byte)Randoms.Next(50, 255)
                    );
                }
                var color = ClassColor[detection.ClassId];
                var cvcolor = new Scalar(color.B, color.G, color.R);
                //바운딩 박스 그리기
                var rect = new OpenCvSharp.Rect((int)detection.X, (int)detection.Y, (int)detection.Width, (int)detection.Height);
                Cv2.Rectangle(frame, rect, cvcolor, 3);
                //라벨 텍스트 생성
                if (ShowedLabel || ShowedConfidence)
                {
                    string label = "";
                    if (ShowedLabel)
                    {
                        label += detection.ClassName;
                    }
                    if (ShowedLabel && ShowedConfidence)
                    { 
                         label += " ";
                    }
                    if (ShowedConfidence)
                    { 
                       label += $"{detection.Confidence:P0}";
                    }
                    // 텍스트 배경
                    int baseLine = 0;
                    var textSize = Cv2.GetTextSize(label, HersheyFonts.HersheySimplex, 0.6, 2, out baseLine);
                    var textRext = new OpenCvSharp.Rect(
                        (int)detection.X,
                        (int)detection.Y - textSize.Height - 10,
                        textSize.Width,
                        textSize.Height + baseLine);
                    Cv2.Rectangle(frame, textRext, cvcolor, -1);
                   //텍스트
                   Cv2.PutText(frame, label, new OpenCvSharp.Point((int)detection.X+5, (int)detection.Y - 5),
                        HersheyFonts.HersheySimplex, 0.6, Scalar.White, 2);
                }
            }
            return frame;
        }
        /// <summary>
        /// 기본 저장
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SaveImageButton_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentDetectionFrame == null)
            {
                MessageBox.Show("저장할 이미지가 없습니다.", "알림",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var saveFileDialog = new SaveFileDialog
            {
                Filter = "PNG 이미지|*.png|JPEG 이미지|*.jpg|BMP 이미지|*.bmp|모든 파일|*.*",
                Title = "이미지 저장",
                FileName = $"detection_{DateTime.Now:yyyyMMdd_HHmmss}.png"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    Cv2.ImWrite(saveFileDialog.FileName, CurrentDetectionFrame);
                    Log($"💾 이미지 저장: {System.IO.Path.GetFileName(saveFileDialog.FileName)}");
                    MessageBox.Show("이미지가 저장되었습니다.", "완료",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"저장 오류: {ex.Message}", "오류",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    Log($"❌ 저장 오류: {ex.Message}");
                }
            }
        }
        /// <summary>
        /// 모든 프레임 저장
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private async void SaveAllFramesButton_Click(object sender, RoutedEventArgs e)
        {
            if (Capture == null || !Capture.IsOpened())
            {
                MessageBox.Show("비디오가 열려있지 않습니다.", "알림",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
            }
            var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                Description = "프레임을 저장할 폴더 선택",
                ShowNewFolderButton = true
            };

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                string folderPath = dialog.SelectedPath;

                var result = MessageBox.Show(
                    $"비디오의 모든 프레임({TotalFrames}개)을 저장하시겠습니까?\n" +
                    $"저장 위치: {folderPath}\n\n" +
                    "시간이 오래 걸릴 수 있습니다.",
                    "확인", MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    await SaveAllFramesAsync(folderPath);
                }
            }
        }
        private async Task SaveAllFramesAsync(string folderPath)
        {
            try
            {
                if (Capture == null || !Capture.IsOpened())
                {
                    MessageBox.Show("비디오가 열려있지 않습니다.", "오류");
                    return;
                }

                // 현재 재생 중지
                bool wasPlaying = CancellationTokenSource != null && !CancellationTokenSource.IsCancellationRequested;


                if (wasPlaying)
                {
                    StopVideoCapture();
                    await Task.Delay(500); // 잠시 대기
                }
                // 비디오 다시 열기
                Capture.Set(VideoCaptureProperties.PosFrames, 0);
                Log($"🎞️ 모든 프레임 저장 시작...");
                StatusText.Text = "프레임 저장 중...";
                StatusText.Visibility = Visibility.Visible;
                int savedCount = 0;
                Mat frame = new Mat();
                while (Capture.Read(frame) && !frame.Empty())
                {
                    // 활성 감지 모델
                    var detections = Manager?.ActiveModel?.Detect(frame);
                    var resultFrame = DrawDetections(frame.Clone(), detections ?? new List<Detection>());

                    // 저장
                    string filename = System.IO.Path.Combine(folderPath, $"frame_{savedCount:D5}.png");
                    Cv2.ImWrite(filename, resultFrame);

                    savedCount++;

                    // UI 업데이트
                    if (savedCount % 10 == 0)
                    {
                        Dispatcher.Invoke(() =>
                        {
                            StatusText.Text = $"저장 중... {savedCount}/{TotalFrames}";
                        });
                    }
                    resultFrame.Dispose();
                }
                frame.Dispose();
                Capture.Set(VideoCaptureProperties.PosFrames, 0);
                StatusText.Visibility = Visibility.Collapsed;
                Log($"✅ 프레임 저장 완료: {savedCount}개");
                MessageBox.Show($"{savedCount}개의 프레임이 저장되었습니다!\n위치: {folderPath}",
                    "완료", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                Log($"❌ 프레임 저장 오류: {ex.Message}");
                MessageBox.Show($"프레임 저장 중 오류: {ex.Message}", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }


        }
        /// <summary>
        /// 조건부 저장 (트리거) 설정
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void TriggerSaveButton_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new TrrigerSetting();
            if (dialog.ShowDialog() == true)
            {
                TriggerSaveEnabled = dialog.IsEnabled;
                TriggerClasses = dialog.SelectedClasses;
                TriggerSavePath = dialog.SavePath;

                if (TriggerSaveEnabled)
                {
                    string classList = string.Join(", ", TriggerClasses);
                    TriggerStatusText.Text = $"트리거: {classList}";
                    TriggerStatusText.Foreground = new SolidColorBrush(Colors.Green);
                    Log($"🎯 트리거 활성화: {classList}");
                }
                else
                {
                    TriggerStatusText.Text = "트리거: 없음";
                    TriggerStatusText.Foreground = new SolidColorBrush(Colors.Gray);
                    Log("🎯 트리거 비활성화");
                }
            }
        }
        /// <summary>
        /// 자동 저장 실행
        /// </summary>
        /// <param name="detections"></param>
        private void AutoSaveDetection(List<Detection> detections)
        {
            try
            {
                string filename = System.IO.Path.Combine(AutoSavePath,
                    $"auto_{DateTime.Now:yyyyMMdd_HHmmss}_{AutoSaveCounter:D4}.png");
                Cv2.ImWrite(filename, CurrentDetectionFrame);
                AutoSaveCounter++;
            }
            catch (Exception ex)
            {
                Log($"❌ 자동 저장 오류: {ex.Message}");
            }
        }

        /// <summary>
        /// 트리거 저장 실행
        /// </summary>
        /// <param name="detections"></param>
        private void TriggerSaveDetection(List<Detection> detections)
        {
            try
            {
                // 트리거 클래스가 감지되었는지 확인
                var triggeredDetections = detections
                    .Where(d => TriggerClasses.Contains(d.ClassName))
                    .ToList();

                if (triggeredDetections.Count > 0)
                {
                    string classList = string.Join("_", triggeredDetections
                        .Select(d => d.ClassName)
                        .Distinct());

                    string filename = System.IO.Path.Combine(TriggerSavePath,
                        $"trigger_{classList}_{DateTime.Now:yyyyMMdd_HHmmss}.png");

                    Cv2.ImWrite(filename, CurrentDetectionFrame);

                    // 메타데이터 JSON 저장
                    SaveDetectionMetadata(filename, triggeredDetections);

                    Log($" 트리거 저장: {classList} 감지! → {System.IO.Path.GetFileName(filename)}");
                }
            }
            catch (Exception ex)
            {
                Log($"❌ 트리거 저장 오류: {ex.Message}");
            }
        }
        /// <summary>
        /// 감지 메타데이터 JSON 저장
        /// </summary>
        /// <param name="imageFilename"></param>
        /// <param name="detections"></param>
        private void SaveDetectionMetadata(string imageFilename, List<Detection> detections)
        {
            try
            {
                var metadata = new
                {
                    Timestamp = DateTime.Now,
                    ImageFile =System.IO.Path.GetFileName(imageFilename),
                    TotalDetections = detections.Count,
                    Detections = detections.Select(d => new
                    {
                        d.ClassName,
                        d.ClassId,
                        Confidence = d.Confidence,
                        BoundingBox = new
                        {
                            X = (int)d.X,
                            Y = (int)d.Y,
                            Width = (int)d.Width,
                            Height = (int)d.Height
                        }
                    }).ToList()
                };

                string jsonFilename = System.IO.Path.ChangeExtension(imageFilename, ".json");
                string jsonText = JsonConvert.SerializeObject(metadata, new JsonSerializerSettings
                {
                    Formatting = Formatting.Indented
                });
                System.IO.File.WriteAllText(jsonFilename, jsonText);
            }
            catch (Exception ex)
            {
                Log($" 메타데이터 저장 오류: {ex.Message}");
            }
        }

        /// <summary>
        /// 비디오 스탑
        /// </summary>
        private void StopVideoCapture()
        {
            CancellationTokenSource?.Cancel();
            Capture?.Release();
            Capture?.Dispose();

            VideoProgressPanel.Visibility = Visibility.Collapsed;
            CurrentFrameNumber = 0;

            ResetWebcamButtons();
            Log("⏹ 비디오 중지");
        }

        private void ResetWebcamButtons()
        {
            StartWebcamButton.IsEnabled = true;
            StopWebcamButton.IsEnabled = false;
            LoadImageButton.IsEnabled = true;
            LoadVideoButton.IsEnabled = true;
            CameraComboBox.IsEnabled = true;
            ObjectDetectionRadio.IsEnabled = true;
            ConfidenceSlider.IsEnabled = true;
            if (Manager?.IsModelLoaded(YoloModelType.FaceDetection) == true)
            {
                FaceDetectionRadio.IsEnabled = true;
            }
        }
        private void Log(string message)
        {
            var timestamp = DateTime.Now.ToString("HH:mm:ss");
            LogTextBlock.Text += $"[{timestamp}] {message}\n";

            // 자동 스크롤
            LogScrollViewer.ScrollToEnd();
        }
        private string FormatTime(double seconds)
        {
            if (double.IsNaN(seconds) || double.IsInfinity(seconds))
                return "00:00";

            TimeSpan time = TimeSpan.FromSeconds(seconds);
            if (time.TotalHours >= 1)
            {
                return time.ToString(@"hh\:mm\:ss");
            }
            return time.ToString(@"mm\:ss");
        }
        /// <summary>
        /// 종류
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            StopVideoCapture();

            // ⭐ 모든 모델 해제
            Manager?.Dispose();

            CurrentFrame?.Dispose();
            CurrentDetectionFrame?.Dispose();
        }

    }
}
