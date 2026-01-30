using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Web.UI.WebControls;

namespace wpfCCTV.Models
{
    /// <summary>
    /// yolo객체 감지 엔진
    /// </summary>
    internal class YoloModel : IDisposable
    {
        /// <summary>
        /// ONNX 모델을 “실제로 실행하는 엔진
        /// </summary>
        private InferenceSession Session;
        private readonly YoloSettings Settings;
        private string[] ClassNames =Array.Empty<string>();
        public YoloModel(YoloSettings settings)
        {
            Settings = settings;
            Initialize();
            LoadClassNames();
        }

        /// <summary>
        /// 초기화
        /// </summary>
        private void Initialize()
        {
            if (!File.Exists(Settings.ModelPath))
            { 
                throw new FileNotFoundException("모델 파일을 찾을 수 없습니다.",Settings.ModelPath);
            }
            var sessionOptions = new SessionOptions();
            if (Settings.UseGpu)
            {
                // 쿠다를 쓰겠다
                sessionOptions.AppendExecutionProvider_CUDA(0);
            }
            Session = new InferenceSession(Settings.ModelPath, sessionOptions);
        }
        /// <summary>
        /// 로드 클래스 네임
        /// </summary>
        private void LoadClassNames()
        {
            if (!File.Exists(Settings.ClassNamesPath))
            {
                throw new FileNotFoundException($"클래스 이름 파일을 찾을 수 없습니다: {Settings.ClassNamesPath}");
            }

            ClassNames = File.ReadAllLines(Settings.ClassNamesPath)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .ToArray();
        }
        /// <summary>
        /// 이미지에서 객체 감지 수행
        /// </summary>
        public List<Detection> Detect(Mat image)
        {
            if (Session == null)
            {
                throw new InvalidOperationException("YOLO 모델이 초기화되지 않았습니다.");
            }
            // 이미지 전처리
            var input = PreprocessImage(image);
            //YOLO추론 실행
            var inputs = new List<NamedOnnxValue>
            {
                // NamedOnnxValue는 “ONNX 모델에 넘기는 입력/출력 데이터의 포장 객체
                NamedOnnxValue.CreateFromTensor("images", input)
            };
             float[] output = null;
            using (var results = Session.Run(inputs))
            {
                output = results.First().AsEnumerable<float>().ToArray();
            }
            List<Detection> detections = new List<Detection>();
            //후처리 (결과 파싱 및 nmsw적용)
            if (output != null)
            {
                detections = PostprocessOutput(output, image.Width, image.Height);
            }
          
             return detections;
        }
        /// <summary>
        /// 이미지 전처리 리사이즈및 정규화
        /// </summary>
        private DenseTensor<float> PreprocessImage(Mat image)
        {
            //1. 이미지 리사이즈
            Mat resized = new Mat();
            float scale = Math.Min(
                          (float) Settings.InputWidth/ image.Width,
                          (float) Settings.InputHeight/ image.Height
                );
            int newWidth = (int)(image.Width * scale);
            int newHeight = (int)(image.Height * scale);
            Cv2.Resize(image, resized, new Size(newWidth, newHeight));
            //패딩추가(중앙정렬)
            Mat padded = new Mat(new Size(Settings.InputWidth, Settings.InputHeight), MatType.CV_8UC3, new Scalar(114, 114, 114));
            int x = (Settings.InputWidth - newWidth) / 2;
            int y = (Settings.InputHeight - newHeight) / 2;
            resized.CopyTo(new Mat(padded, new Rect(x, y, newWidth, newHeight)));
            //bgr -rgb 변환
            Mat rgb = new Mat();
            Cv2.CvtColor(padded, rgb, ColorConversionCodes.BGR2RGB);
            //정규화
            var tensor = new DenseTensor<float>(new[] { 1, 3, Settings.InputHeight, Settings.InputWidth });
            for (int y_pos = 0; y_pos < Settings.InputHeight; y_pos++)
            {
                for (int x_pos = 0; x_pos < Settings.InputWidth; x_pos++)
                { 
                     var pixel =rgb.At<Vec3b>(y_pos, x_pos);
                    tensor[0, 0, y_pos, x_pos] = pixel[0] / 255.0f; // R
                    tensor[0, 1, y_pos, x_pos] = pixel[1] / 255.0f; // G
                    tensor[0, 2, y_pos, x_pos] = pixel[2] / 255.0f; // B
                }
            }
            resized.Dispose();
            padded.Dispose();
            rgb.Dispose();
            return tensor;
        }
        /// <summary>
        /// YOLO 출력 후처리 (멀티 모델 지원)
        /// </summary>
        private List<Detection> PostprocessOutput(float[] output, int originalWidth, int originalHeight)
        { 
            var detections = new List<Detection>();
            if (output == null || output.Length == 0)
            {
                return detections;
            }
            // YOLOv8 출력 형식: [1, 84, 8400]
            // 84 = 4(bbox) + 80(classes)
            // 8400 = 감지 후보 수
            // YOLOv12n-face: 4(bbox) + 1(face) = 5
            int dimension = 4 + Settings.ClassCount;// 4 + 클래스수
            if (dimension <= 4)
            {
                throw new InvalidOperationException($"잘못된 ClassCount: {Settings.ClassCount}");
            }
            int row =output.Length / dimension;
            if (row <= 0)
            {
                return detections;
            }
            float scalex = (float)originalWidth / Settings.InputWidth;
            float scaley = (float)originalHeight / Settings.InputHeight;

            for (int i = 0; i < row; i++)
            { 
                 int index = i * dimension;

                // 인덱스 범위 체크
                if (index + dimension > output.Length)
                {
                    break;
                }
                // 바운딩 박스 정보 (중심 좌표 + 너비/높이)
                float centerx = output[index];
               float centery = output[index + 1];
               float width = output[index + 2];
               float height = output[index + 3];
                // 클래스별 확률 찾기
                float maxConfidence = 0;
                int maxClassid = 0;
                // 모델 타입에 따라 다르게 처리
                if (Settings.ModelType == YoloModelType.FaceDetection)
                {
                    // 얼굴 인식 : 클래스 1개만
                    maxConfidence = output[index + 4];
                    maxClassid = 0; // 얼굴 클래스 id는 0
                }
                else
                { 
                    // 일반 객체 감지 : 여러 클래스 중 최대값 찾기
                    for (int c = 0; c < Settings.ClassCount; c++)
                    {
                        float confidence = output[index + 4 + c];
                        if (confidence > maxConfidence)
                        {
                            maxConfidence = confidence;
                            maxClassid = c;
                        }
                    }
                }
                // 신뢰도 임계값 체크
                if (maxConfidence < Settings.ConfidenceThreshold)
                    continue;
                // ⭐ ClassId 유효성 체크
                if (maxClassid < 0 || maxClassid >= ClassNames.Length)
                {
                    // 범위 벗어난 ClassId는 무시
                    continue;
                }
                // 중심 좌표 -> 최상단 좌표 변환 및 스케일 조절
                float x = (centerx - width / 2) * scalex;
                float y = (centery - height / 2) * scaley;
                width *= scalex;
                height *= scaley;
                if (width <= 0 || height <= 0 || x < 0 || y < 0 ||
                  x + width > originalWidth || y + height > originalHeight)
                {
                    // 잘못된 박스는 무시
                    continue;
                }
                detections.Add( new Detection
                {
                    ClassId = maxClassid,
                    ClassName = maxClassid < ClassNames.Length ? ClassNames[maxClassid] : $"Class_{maxClassid}",
                    Confidence = maxConfidence,
                    X = x,
                    Y = y,
                    Width = width,
                    Height = height
                });
            }
            // NMS (Non-Maximum Suppression) 적용
            return ApplyNMS(detections);
        }
        /// <summary>
        ///  NMS (Non-Maximum Suppression): 겹치는 박스 제거
        /// </summary>
        private List<Detection> ApplyNMS(List<Detection> detections)
        {
            var finalDetections = new List<Detection>();
            // 클래스별로 NMS 적용
            var groupedDetections = detections.GroupBy(d => d.ClassId);
            foreach (var group in groupedDetections)
            {
                var dets = group.OrderByDescending(d => d.Confidence).ToList();
                while (dets.Count > 0)
                {
                    var best = dets[0];
                    finalDetections.Add(best);
                    dets.RemoveAt(0);
                    dets = dets.Where(d => d.ClassId != best.ClassId || CalculateIoU(best, d) < Settings.NmsThreshold).ToList();
                }
            }
            return finalDetections;
        }
        /// <summary>
        /// IoU (Intersection over Union) 계산
        /// </summary>
        private float CalculateIoU(Detection a, Detection b)
        {
            float x1 = Math.Max(a.X, b.X);
            float y1 = Math.Max(a.Y, b.Y);
            float x2 = Math.Min(a.X + a.Width, b.X + b.Width);
            float y2 = Math.Min(a.Y + a.Height, b.Y + b.Height);
            float intersection = Math.Max(0, x2 - x1) * Math.Max(0, y2 - y1);
            float union = (a.Width * a.Height) + (b.Width * b.Height) - intersection;
            return union > 0 ? intersection / union : 0;
        }

        /// <summary>
        /// 신뢰도 임계값을 실시간으로 변경
        /// </summary>
        public void SetConfidenceThreshold(float threshold)
        {
            if (threshold < 0.0f || threshold > 1.0f)
            {
                throw new ArgumentOutOfRangeException(nameof(threshold),
                    "임계값은 0.0~1.0 사이여야 합니다.");
            }
            Settings.ConfidenceThreshold = threshold;
        }

        /// <summary>
        /// 현재 신뢰도 임계값 가져오기
        /// </summary>
        public float GetConfidenceThreshold()
        {
            return Settings.ConfidenceThreshold;
        }
        public void Dispose()
        {
            Session?.Dispose();
        }
    }
}
