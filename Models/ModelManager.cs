using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace wpfCCTV.Models
{
    internal class ModelManager :IDisposable
    {
        private readonly Dictionary<YoloModelType, YoloModel> Models;
        private YoloModelType ModelType;

        public YoloModelType ActiveModelType => ModelType;
        public YoloModel ActiveModel => Models.ContainsKey(ActiveModelType) ? Models[ActiveModelType]: null;

        public ModelManager()
        {
            Models = new Dictionary<YoloModelType, YoloModel>();
            ModelType = YoloModelType.ObjectDetection;
        }
        /// <summary>
        /// 로드 모델
        /// </summary>
        public void LoadModel(YoloSettings settings)
        {
            if (Models.ContainsKey(settings.ModelType))
            {
                Models[settings.ModelType].Dispose();
                Models.Remove(settings.ModelType);
            }
            var model = new YoloModel(settings);
            Models[settings.ModelType] = model;
        }
        /// <summary>
        /// 활성 모델 전환
       /// </summary>
        public void SwitchModel(YoloModelType modelType)
        {
            if (!Models.ContainsKey(modelType))
            { 
               throw new Exception("모델이 로드되지 않았습니다.");
            }
            ModelType = modelType;
        }
        /// <summary>
        /// 모델 로드 되었는지 확인
        /// </summary>
        public bool IsModelLoaded(YoloModelType modelType)
        {
            return Models.ContainsKey(modelType);
        }
        /// <summary>
        /// 모든 도드된 모델 타입 가져오기
        /// </summary>
        public IEnumerable<YoloModelType> GetLoadedModelTypes()
        {
            return Models.Keys;
        }
        public void Dispose()
        {
            foreach (var model in Models.Values)
            {
                model.Dispose();
            }
            Models.Clear();
        }
    }
}
