using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace wpfCCTV.Models
{
    /// <summary>
    /// YOLO 모델 타입
    /// </summary>
    public enum YoloModelType
    {
        ObjectDetection,  // 일반 객체 감지 (YOLOv8)
        FaceDetection     // 얼굴 감지 (YOLOv12n-face)
    }
    /// <summary>
    /// YOLO 모델 설정
    /// </summary>
    public class YoloSettings
    {
        /// <summary>
        /// 모델 타입
        /// </summary>
        public YoloModelType ModelType { get; set; } = YoloModelType.ObjectDetection;

        /// <summary>
        /// YOLO 모델 파일 경로 (.onnx)
        /// </summary>
        public string ModelPath { get; set; } = "Assets/yolov8n.onnx";

        /// <summary>
        /// 클래스 이름 파일 경로 (.names 또는 .txt)
        /// </summary>
        public string ClassNamesPath { get; set; } = "Assets/coco.names";

        /// <summary>
        /// 입력 이미지 너비 (YOLOv8: 640, YOLOv12n-face: 640)
        /// </summary>
        public int InputWidth { get; set; } = 640;

        /// <summary>
        /// 입력 이미지 높이
        /// </summary>
        public int InputHeight { get; set; } = 640;

        /// <summary>
        /// 신뢰도 임계값 (0.0 ~ 1.0)
        /// </summary>
        public float ConfidenceThreshold { get; set; } = 0.5f;

        /// <summary>
        /// NMS (Non-Maximum Suppression) IoU 임계값
        /// </summary>
        public float NmsThreshold { get; set; } = 0.3f;

        /// <summary>
        /// GPU 사용 여부 (CUDA 설치 필요)
        /// </summary>
        public bool UseGpu { get; set; } = false;

        /// <summary>
        /// 클래스 수 (COCO: 80, Face: 1)
        /// </summary>
        public int ClassCount { get; set; } = 80;

        /// <summary>
        /// 얼굴 감지 모델용 설정 생성
        /// </summary>
        public static YoloSettings CreateFaceDetectionSettings()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            return new YoloSettings
            {
                ModelType = YoloModelType.FaceDetection,
                ModelPath = System.IO.Path.Combine(baseDir, "Assets", "yolov12n-face.onnx"),
                ClassNamesPath = System.IO.Path.Combine(baseDir, "Assets", "face.names"),
                InputWidth = 640,
                InputHeight = 640,
                ConfidenceThreshold = 0.5f,
                NmsThreshold = 0.45f,
                UseGpu = false,
                ClassCount = 1  // face만
            };
        }

        /// <summary>
        /// 일반 객체 감지 모델용 설정 생성
        /// </summary>
        public static YoloSettings CreateObjectDetectionSettings()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            return new YoloSettings
            {
                ModelType = YoloModelType.ObjectDetection,
                ModelPath = System.IO.Path.Combine(baseDir, "Assets", "yolov8n.onnx"),
                ClassNamesPath = System.IO.Path.Combine(baseDir, "Assets", "coco.names"),
                InputWidth = 640,
                InputHeight = 640,
                ConfidenceThreshold = 0.5f,
                NmsThreshold = 0.45f,
                UseGpu = false,
                ClassCount = 80  // COCO 80개
            };
        }
    }
}
