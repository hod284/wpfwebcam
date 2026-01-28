using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace wpfCCTV.Models
{
    internal class Detection
    {
        // <summary>
        /// 클래스 ID (예: 0=person, 2=car)
        /// </summary>
        public int ClassId { get; set; }

        /// <summary>
        /// 클래스 이름 (예: "person", "car")
        /// </summary>
        public string ClassName { get; set; } = string.Empty;

        /// <summary>
        /// 신뢰도 (0.0 ~ 1.0)
        /// </summary>
        public float Confidence { get; set; }

        /// <summary>
        /// 바운딩 박스 X 좌표 (좌측 상단)
        /// </summary>
        public float X { get; set; }

        /// <summary>
        /// 바운딩 박스 Y 좌표 (좌측 상단)
        /// </summary>
        public float Y { get; set; }

        /// <summary>
        /// 바운딩 박스 너비
        /// </summary>
        public float Width { get; set; }

        /// <summary>
        /// 바운딩 박스 높이
        /// </summary>
        public float Height { get; set; }

        /// <summary>
        /// 바운딩 박스 중심 X 좌표
        /// </summary>
        public float CenterX => X + Width / 2;

        /// <summary>
        /// 바운딩 박스 중심 Y 좌표
        /// </summary>
        public float CenterY => Y + Height / 2;

        public override string ToString()
        {
            return $"{ClassName} ({Confidence:P1}) at ({X:F0}, {Y:F0}, {Width:F0}, {Height:F0})";
        }
    }
}
