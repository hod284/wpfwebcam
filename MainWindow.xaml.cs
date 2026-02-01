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
        private readonly object CaptureLock = new object(); // Capture 동기화용
        private bool IsStoppingCapture = false; // 중복 호출 방지

        // 비디오 프로그레스 바 관련
        private int TotalFrames = 0;
        private int CurrentFrameNumber = 0;
        private double VideoFps = 30;

        // 비디오 재생 제어
        private bool IsPaused = false;
        private bool IsUserSeeking = false;
        private bool IsSeeking = false;
        private string VideoFilePath = "";

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
            InitializeComponent();
            InitializeYoloModel();
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

                if (!Capture.IsOpened())
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
                    VideoFilePath = openFileDialog.FileName;
                    IsPaused = false;
                    // 비디오 캡처 초기화
                    Capture = new VideoCapture(openFileDialog.FileName);
                    if (!Capture.IsOpened())
                    {
                        MessageBox.Show("비디오 파일을 열 수 없습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    // 비디오 정보 가져오기
                    StatusText.Visibility = Visibility.Collapsed;
                    TotalFrames = (int)Capture.Get(VideoCaptureProperties.FrameCount);
                    VideoFps = Capture.Get(VideoCaptureProperties.Fps);
                    if(VideoFps<=0)
                        VideoFps = 30;
                    VideoProgressBar.Maximum = TotalFrames>0? TotalFrames:100;
                    VideoProgressBar.Value = 0;
                    CurrentFrameNumber = 0;
                    TotalTimeText.Text = FormatTime(TotalFrames / VideoFps);
                    PlayPauseButton.Content = "⏸ 일시정지";
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
            while (!token.IsCancellationRequested)
            {
                // 일시정지 중이면 루프 대기
                while (IsPaused && !token.IsCancellationRequested)
                    Thread.Sleep(50);
                if (token.IsCancellationRequested) break;

                try
                {
                    VideoCapture captureRef;
                    lock (CaptureLock)
                    {
                        if (Capture == null || Capture.IsDisposed)
                            break;
                        captureRef = Capture;
                    }

                    CurrentFrame = new Mat();
                    if (!captureRef.Read(CurrentFrame) || CurrentFrame.Empty())
                    { 
                         Dispatcher.Invoke(() =>Log("비디오 종료"));
                        CurrentFrame.Dispose();
                        break;
                    }
                    CurrentFrameNumber++;
                    //  1. 먼저 원본 프레임을 즉시 화면에 표시
                    Dispatcher.Invoke(() =>
                    {
                        try
                        {
                            DisplayImage.Source = CurrentFrame.ToBitmapSource();
                        }
                        catch { /* 무시 */ }
                    });
                    // 프로그래스바 업데이트 (사용자가 스크러버를 드래그 중이면 건너뜀)
                    Dispatcher.Invoke(() =>
                    {
                        if (!IsUserSeeking && VideoProgressPanel.Visibility == Visibility.Visible && TotalFrames > 0)
                        {
                            VideoProgressBar.Value = CurrentFrameNumber;
                            CurrentTimeText.Text = FormatTime(CurrentFrameNumber / VideoFps);
                        }
                    });

                    // ✅ 프레임 복사본을 전달
                    var frame = CurrentFrame;
                    Dispatcher.Invoke(() =>  DetectAndDisplayAsync(frame));

                }
                catch (Exception ex)
                {
                    Dispatcher.Invoke(() => Log($"❌ 프레임 처리 오류: {ex.Message}"));
                    break;
                }
            }
            if (!IsSeeking)
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
                var detections =   Manager.ActiveModel.Detect(frame);
                if (detections == null)
                {
                    Log("⚠️ 감지 결과가 null입니다");
                    return;
                }
                // ⭐ 너무 많은 감지 결과 제한 (중첩 방지)
                if (detections.Count > 50)
                {
                    Log($"⚠️ 너무 많은 감지: {detections.Count}개 → 상위 50개만 사용");
                    detections = detections
                        .OrderByDescending(d => d.Confidence)
                        .Take(50)
                        .ToList();
                }

                var elapsedMs = (DateTime.Now - start).TotalMilliseconds;
                //  Detection 객체 유효성 검증
                var validDetections = detections.Where(d =>
               d != null &&
               !string.IsNullOrEmpty(d.ClassName) &&
               d.ClassId >= 0 &&
               d.Confidence > 0
           ).ToList();

                if (validDetections.Count != detections.Count)
                {
                    Log($"⚠️ 잘못된 감지 결과 제외: {detections.Count - validDetections.Count}개");
                }
                // 이전 프레임 확실히 해제
                if (CurrentDetectionFrame != null && !CurrentDetectionFrame.IsDisposed)
                {
                    CurrentDetectionFrame.Dispose();
                    CurrentDetectionFrame = null;
                }
                // 감지 결과 그리기
                CurrentDetectionFrame?.Dispose();
                CurrentDetectionFrame = DrawDetections(frame.Clone(), detections);
                // wpf 컨트롤 에 표시
                if (CurrentDetectionFrame != null && !CurrentDetectionFrame.Empty())
                {
                    DisplayImage.Source = CurrentDetectionFrame.ToBitmapSource();
                }
                //통계업데이트 
                UpdateStatistics(detections,elapsedMs);
                // 트리거 저장 처리
                if (TriggerSaveEnabled && detections.Count > 0&& AutoSaveEnabled)
                {
                    TriggerSaveDetection(detections);
                }
                // 자동 저장 처리
                else if (AutoSaveEnabled && detections.Count > 0)
                {
                    AutoSaveDetection(detections);
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
                    ClassCountTotal[item.Class] = item.Count;
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
                if (!ClassColor.ContainsKey(detection.ClassId))
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
                var rect = new OpenCvSharp.Rect((int)detection.X, (int)detection.Y, (int)detection.Width , (int)detection.Height );
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
        /// 재생 / 일시정지 토글
        /// </summary>
        private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
        {
            IsPaused = !IsPaused;
            PlayPauseButton.Content = IsPaused ? "▶ 재생" : "⏸ 일시정지";
            Log(IsPaused ? "⏸ 일시정지" : "▶ 재생 재개");
        }

        /// <summary>
        /// 비디오 완전 중지 (Stop 버튼)
        /// </summary>
        private void StopVideoButton_Click(object sender, RoutedEventArgs e)
        {
            StopVideoCapture();
        }

        /*
         ● ← 이게 Thumb
         WPF 마우스 이벤트는 두 단계가 있음.
        Preview (터널링)  : Window → Control → Child
        Mouse (버블링)   : Child → Control → Window
        그냥 MouseLeftButtonDown/Up 이벤트를 슬경우
        마우스 이벤트는 포커스가 있는 컨트롤 기준으로 발화하는데, thumb를 잡고 드래그할 때 시스템이 마우스 위치를 매 프레임마다 체크합니다. 그리고 그 순간 좌표가 thumb의 히트테스트 영역에서 1픽셀이라도 벗어나면 포커스가 바뀌고 MouseUp이 thumb가 아닌 다른 곳에서 발화하거나, 아예 안 오는 경우가 됩니다.
        사람 눈에서는 "내가 thumb 위에서 놓았다"고 느껴져도, 시스템은 마우스 좌표를 픽셀 단위로 추적하고 있으니까 그 차이가 발생하는 거죠.
        Preview 이벤트는 이런 문제가 없는 게, 이건 컨트롤 자체 전체 영역을 기준으로 잡으니까 thumb 안에든 밖에든 Slider 영역 안에 있는 것만으로는 충분합니다. 마우스가 Slider 밖으로 완전히 나가지 않는 한 빠짐없이 잡히는 거예요.
         */
        /// <summary>
        /// 스크러버 클릭/드래그 시작
        /// </summary>
        private void VideoProgressBar_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            IsUserSeeking = true;
            // 클릭한 위치로 즉시 값 이동
            var slider = (Slider)sender;
            double pos = e.GetPosition(slider).X / slider.ActualWidth;
            slider.Value = slider.Minimum + pos * (slider.Maximum - slider.Minimum);
        }

        /// <summary>
        /// 스크러버 클릭/드래그 끝 → seek 실행
        /// </summary>
        private void VideoProgressBar_PreviewMouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            SeekToFrame((int)VideoProgressBar.Value);
            IsUserSeeking = false;
        }

        /// <summary>
        /// 지정된 프레임 번호로 탐색
        /// </summary>
        private void SeekToFrame(int frameNumber)
        {
            if (string.IsNullOrEmpty(VideoFilePath)) return;

            bool wasPaused = IsPaused;

            // 기존 루프가 종료될 때 StopVideoCapture를 호출하지 않도록 플래그
            IsSeeking = true;

            // 기존 스트림 중단
            CancellationTokenSource?.Cancel();
            // 기존 루프가 종료될 시간 확보
            Thread.Sleep(100);

            lock (CaptureLock)
            {
                if (Capture != null && !Capture.IsDisposed)
                    Capture.Dispose();
                Capture = null;
            }

            // 새로운 캡처를 열고 원하는 프레임으로 점프
            Capture = new VideoCapture(VideoFilePath);
            if (!Capture.IsOpened())
            {
                IsSeeking = false;
                return;
            }

            Capture.Set(VideoCaptureProperties.PosFrames, frameNumber);
            CurrentFrameNumber = frameNumber;
            IsPaused = wasPaused;

            Dispatcher.Invoke(() =>
            {
                VideoProgressBar.Value = frameNumber;
                CurrentTimeText.Text = FormatTime(frameNumber / VideoFps);
            });

            // 플래그 복원 후 스트림 재시작
            IsSeeking = false;
            CancellationTokenSource = new CancellationTokenSource();
            Task.Run(() => ProcessVideoStream(CancellationTokenSource.Token));
        }

        /// <summary>
        /// 비디오 스탑
        /// </summary>
        private void StopVideoCapture()
        {
            lock (CaptureLock)
            {
                // 이미 중지 중이면 리턴
                if (IsStoppingCapture)
                    return;
                IsStoppingCapture = true;


                try
                {
                    CancellationTokenSource?.Cancel();

                    if (Capture != null && !Capture.IsDisposed)
                    {
                        Capture.Dispose();
                    }
                    Capture = null;

                    VideoProgressPanel.Visibility = Visibility.Collapsed;
                    CurrentFrameNumber = 0;
                    IsPaused = false;
                    PlayPauseButton.Content = "⏸ 일시정지";
                    VideoFilePath = "";

                    ResetWebcamButtons();
                    Log("⏹ 비디오 중지");
                }
                finally
                {
                    IsStoppingCapture = false;
                }
            }// LOCK을 했을때 자동으로 이부분에서 쓰레드키를 반납 만약 수동으로 할 경우 Monitor.Exit(CaptureLock);을 써야함
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
