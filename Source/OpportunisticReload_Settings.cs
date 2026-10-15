using Verse;

namespace TSA.CE.OpportunisticReloadThreshold
{
	public class OpportunisticReload_Settings : ModSettings
	{
		public const float DefaultThresholdPercent = 80f;
		public const float MinThresholdPercent = 0f;
		public const float MaxThresholdPercent = 100f;

		/// <summary>Magazine fill % at which opportunistic reload triggers (0–100).</summary>
		public float thresholdPercent = DefaultThresholdPercent;

		public static OpportunisticReload_Settings? Current;

		public float ThresholdFraction => thresholdPercent / 100f;

		public override void ExposeData()
		{
			Scribe_Values.Look(ref thresholdPercent, "thresholdPercent", DefaultThresholdPercent);
			EnforceBounds();
		}

		public void ResetToDefaults()
		{
			thresholdPercent = DefaultThresholdPercent;
		}

		public void EnforceBounds()
		{
			thresholdPercent = UnityEngine.Mathf.Clamp(thresholdPercent, MinThresholdPercent, MaxThresholdPercent);
		}
	}
}
