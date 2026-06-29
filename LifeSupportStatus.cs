using System;
namespace KickLifeSupport
{
    public class LifeSupportStatus
    {
        public string lsStatus = "Nominal";
        public float cabinCO2 = 0;
        public double lowO2Time = 0f;
        public double ambientExposureTime = 0f;
        public double ambientExposureRemaining = -1f;
        public double lowWaterTime = 0f;
        public double lowFoodTime = 0f;
        public double lowClimateTime = 0f;
        public double tempRangeTime = 0f;
        public bool breathingGraceAnnounced = false;
        public bool ambientGraceAnnounced = false;
        public bool waterGraceAnnounced = false;
        public bool foodGraceAnnounced = false;
        public bool climateGraceAnnounced = false;
        public bool tempGraceAnnounced = false;

        /// <summary>
        /// The cabin temp reported when outside of the "safe" range.
        /// This should not be used for anything other than reporting purposes.
        /// </summary>
        /// <remarks>
        /// DO NOT use for monitoring. If you need the actual current cabin temperature, get
        /// <code>KickLifeSupportModule.cabinTemp</code>.
        /// </remarks>
        public double lastCabinTemp = 22f;

        public double lastUpdateTime = 0;

        public double lastLiOHScrubAmount = 0f;
        public double lastOpenLoopVentedAmount = 0f;
        public double activeOpenLoopVentCapacity = 0f;
        public int activeLiOHScrubberCount = 0;

        public void Save(ConfigNode node)
        {
            node.AddValue("CabinCO2", cabinCO2);
            node.AddValue("LowO2Time", lowO2Time);
            node.AddValue("AmbientExposureTime", ambientExposureTime);
            node.AddValue("LowWaterTime", lowWaterTime);
            node.AddValue("LowFoodTime", lowFoodTime);
            node.AddValue("LowClimateTime", lowClimateTime);
            node.AddValue("TempRangeTime", tempRangeTime);
            node.AddValue("BreathingGraceAnnounced", breathingGraceAnnounced);
            node.AddValue("AmbientGraceAnnounced", ambientGraceAnnounced);
            node.AddValue("WaterGraceAnnounced", waterGraceAnnounced);
            node.AddValue("FoodGraceAnnounced", foodGraceAnnounced);
            node.AddValue("ClimateGraceAnnounced", climateGraceAnnounced);
            node.AddValue("TempGraceAnnounced", tempGraceAnnounced);
        }

        public void Load(ConfigNode node)
        {
            float.TryParse(node.GetValue("CabinCO2"), out cabinCO2);
            double.TryParse(node.GetValue("LowO2Time"), out lowO2Time);
            double.TryParse(node.GetValue("AmbientExposureTime"), out ambientExposureTime);
            double.TryParse(node.GetValue("LowWaterTime"), out lowWaterTime);
            double.TryParse(node.GetValue("LowFoodTime"), out lowFoodTime);
            double.TryParse(node.GetValue("LowClimateTime"), out lowClimateTime);
            double.TryParse(node.GetValue("TempRangeTime"), out tempRangeTime);
            bool.TryParse(node.GetValue("BreathingGraceAnnounced"), out breathingGraceAnnounced);
            bool.TryParse(node.GetValue("AmbientGraceAnnounced"), out ambientGraceAnnounced);
            bool.TryParse(node.GetValue("WaterGraceAnnounced"), out waterGraceAnnounced);
            bool.TryParse(node.GetValue("FoodGraceAnnounced"), out foodGraceAnnounced);
            bool.TryParse(node.GetValue("ClimateGraceAnnounced"), out climateGraceAnnounced);
            bool.TryParse(node.GetValue("TempGraceAnnounced"), out tempGraceAnnounced);
        }
    }
}
