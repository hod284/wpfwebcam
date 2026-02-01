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
        /// ONNX 모델을 "실제로 실행하는 엔진
        /// </summary>
        private InferenceSession Session;
        private readonly YoloSettings Settings;
        private string[] ClassNames = Array.Empty<string>();
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
                throw new FileNotFoundException("모델 파일을 찾을 수 없습니다.", Settings.ModelPath);
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
                // NamedOnnxValue는 "ONNX 모델에 넘기는 입력/출력 데이터의 포장 객체
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
                          (float)Settings.InputWidth / image.Width,
                          (float)Settings.InputHeight / image.Height
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
                    var pixel = rgb.At<Vec3b>(y_pos, x_pos);
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
        /// ⭐ YOLOv8: [1, 84, 8400] → [배치, 속성, 검출]
        /// ⭐ YOLOv12n-face: [1, 360, 5] → [배치, 검출, 속성]
        ///yolo 파일에서  파일 구조 즉 []여기안에 있는 값을 yolo 파일안에 정해져 있으며 정해진대로 넣어주면 그걸 게산해서 확률을 yolo파일에서 우리한테 넘겨줌 그걸우리는 표현해주면 된다
        /// </summary>
        private List<Detection> PostprocessOutput(float[] output, int originalWidth, int originalHeight)
        {
            var detections = new List<Detection>();
            if (output == null || output.Length == 0)
            {
                return detections;
            }

            int numAttributes = 4 + Settings.ClassCount; // bbox(4) + classes
            int numDetections;
            bool isTransposed; // 텐서 형식 판단

            // ⭐ 모델 타입에 따라 텐서 형식 결정
            if (Settings.ModelType == YoloModelType.FaceDetection)
            {
                // YOLOv12n-face: [1, 360, 5] → [배치, 검출, 속성]
                numDetections = output.Length / numAttributes;
                isTransposed = true; // 검출이 먼저, 속성이 나중
                System.Diagnostics.Debug.WriteLine($"🔍 YOLOv12n-face 출력 분석 (Transposed):");
            }
            else
            {
                // YOLOv8: [1, 84, 8400] → [배치, 속성, 검출]
                numDetections = output.Length / numAttributes;
                isTransposed = false; // 속성이 먼저, 검출이 나중
                System.Diagnostics.Debug.WriteLine($"🔍 YOLOv8 출력 분석 (Standard):");
            }

            System.Diagnostics.Debug.WriteLine($"   - 모델 타입: {Settings.ModelType}");
            System.Diagnostics.Debug.WriteLine($"   - 총 출력 길이: {output.Length}");
            System.Diagnostics.Debug.WriteLine($"   - 속성 수 (4 + ClassCount): {numAttributes}");
            System.Diagnostics.Debug.WriteLine($"   - 계산된 감지 후보 수: {numDetections}");
            System.Diagnostics.Debug.WriteLine($"   - 텐서 형식: {(isTransposed ? "[검출, 속성]" : "[속성, 검출]")}");

            float scaleX = (float)originalWidth / Settings.InputWidth;
            float scaleY = (float)originalHeight / Settings.InputHeight;

            // ⭐ 첫 5개 후보의 값을 출력하여 디버깅
            int sampleCount = Math.Min(5, numDetections);
            System.Diagnostics.Debug.WriteLine($"\n📊 첫 {sampleCount}개 감지 후보 샘플:");

            for (int i = 0; i < numDetections; i++)
            {
                float centerX, centerY, width, height, maxConfidence;
                int maxClassId = 0;

                if (isTransposed)
                {
                    // ⭐ YOLOv12n-face 형식: [배치, 검출, 속성]
                    // 평탄화: output[detection_idx * numAttributes + attribute_idx]
                    int baseIdx = i * numAttributes;
                    centerX = output[baseIdx + 0];
                    centerY = output[baseIdx + 1];
                    width = output[baseIdx + 2];
                    height = output[baseIdx + 3];

                    if (Settings.ModelType == YoloModelType.FaceDetection)
                    {
                        maxConfidence = output[baseIdx + 4];
                        maxClassId = 0;
                    }
                    else
                    {
                        maxConfidence = 0;
                        for (int c = 0; c < Settings.ClassCount; c++)
                        {
                            float confidence = output[baseIdx + 4 + c];
                            if (confidence > maxConfidence)
                            {
                                maxConfidence = confidence;
                                maxClassId = c;
                            }
                        }
                    }
                }
                else
                {
                    // ⭐ YOLOv8 형식: [배치, 속성, 검출]
                    // 평탄화: output[attribute_idx * numDetections + detection_idx]
                    centerX = output[0 * numDetections + i];
                    centerY = output[1 * numDetections + i];
                    width = output[2 * numDetections + i];
                    height = output[3 * numDetections + i];

                    maxConfidence = 0;
                    for (int c = 0; c < Settings.ClassCount; c++)
                    {
                        float confidence = output[(4 + c) * numDetections + i];
                        if (confidence > maxConfidence)
                        {
                            maxConfidence = confidence;
                            maxClassId = c;
                        }
                    }
                }

                // ⭐ 디버깅: 처음 몇 개 후보 출력
                if (i < sampleCount)
                {
                    System.Diagnostics.Debug.WriteLine($"   [{i}] cx={centerX:F2}, cy={centerY:F2}, w={width:F2}, h={height:F2}, conf={maxConfidence:F4}, class={maxClassId}");
                }

                // 신뢰도 임계값 체크
                if (maxConfidence < Settings.ConfidenceThreshold)
                    continue;

                // ClassId 유효성 체크
                if (maxClassId < 0 || maxClassId >= ClassNames.Length)
                {
                    continue;
                }

                // 중심 좌표 -> 좌상단 좌표 변환 및 스케일 조절
                float x = (centerX - width / 2) * scaleX;
                float y = (centerY - height / 2) * scaleY;
                width *= scaleX;
                height *= scaleY;

                // 바운딩 박스 유효성 체크
                if (width <= 0 || height <= 0 || x < 0 || y < 0 ||
                    x + width > originalWidth || y + height > originalHeight)
                {
                    continue;
                }

                detections.Add(new Detection
                {
                    ClassId = maxClassId,
                    ClassName = maxClassId < ClassNames.Length ? ClassNames[maxClassId] : $"Class_{maxClassId}",
                    Confidence = maxConfidence,
                    X = x,
                    Y = y,
                    Width = width,
                    Height = height
                });
            }

            System.Diagnostics.Debug.WriteLine($"\n   - 임계값 통과: {detections.Count}개");

            // NMS (Non-Maximum Suppression) 적용
            var finalDetections = ApplyNMS(detections);
            System.Diagnostics.Debug.WriteLine($"   - NMS 후: {finalDetections.Count}개\n");

            return finalDetections;
        }
        /// <summary>
        ///  NMS (Non-Maximum Suppression): 겹치는 박스 제거
        /// </summary>
        private List<Detection> ApplyNMS(List<Detection> detections)
        {
            if (detections.Count == 0)
                return detections;

            var result = new List<Detection>();
            var sortedDetections = detections.OrderByDescending(d => d.Confidence).ToList();

            while (sortedDetections.Count > 0)
            {
                var best = sortedDetections[0];
                result.Add(best);
                sortedDetections.RemoveAt(0);

                sortedDetections = sortedDetections
                    .Where(d => d.ClassId != best.ClassId || CalculateIoU(best, d) < Settings.NmsThreshold)
                    .ToList();
            }

            return result;
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
            float areaA = a.Width * a.Height;
            float areaB = b.Width * b.Height;
            float union = areaA + areaB - intersection;

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
