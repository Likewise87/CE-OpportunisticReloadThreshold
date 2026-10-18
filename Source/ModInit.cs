using System;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;
using CombatExtended;

namespace TSA.CE.OpportunisticReloadThreshold
{
	public enum SliderFormat { Fixed0, Percent }

	public class ModInit : Mod
	{
		public const string Id = "TSA.CE.OpportunisticReloadThreshold";

		private OpportunisticReload_Settings settings;

		public ModInit(ModContentPack content) : base(content)
		{
			settings = GetSettings<OpportunisticReload_Settings>();
			OpportunisticReload_Settings.Current = settings;
			// Do not PatchAll here: Harmony may force-compile CE turret types whose
			// static ctors load Unity textures, and Mod.ctor is not always main-thread.
		}

		[StaticConstructorOnStartup]
		private static class HarmonyBootstrap
		{
			static HarmonyBootstrap()
			{
				new Harmony(Id).PatchAll();
			}
		}

		public override string SettingsCategory() => "CE Reload Magazine Threshold";

		public override void DoSettingsWindowContents(Rect inRect)
		{
			var s = settings;
			var l = new Listing_Standard();
			l.Begin(inRect);

			DrawResetButton(l, () => s.ResetToDefaults());

			l.Label("Default opportunistic reload threshold for Combat Extended magazine weapons and player turrets.");
			l.Gap(6f);
			l.Label("Applied once when a colonist equips a weapon or a player turret initializes, if the threshold is still CE's default (0). You can still fine-tune each weapon via the ammo gizmo.");
			l.Gap(8f);

			s.thresholdPercent = LabeledSlider(
				l,
				"Reload threshold",
				s.thresholdPercent,
				OpportunisticReload_Settings.MinThresholdPercent,
				OpportunisticReload_Settings.MaxThresholdPercent,
				"Reload when magazine ammo falls to this percentage of magazine size.\n0% = CE default (empty / no opportunistic default).\n80% = reload when 20% of the mag has been fired.",
				1f,
				SliderFormat.Percent,
				OpportunisticReload_Settings.DefaultThresholdPercent);

			l.End();
			s.EnforceBounds();
		}

		private static void DrawResetButton(Listing_Standard l, Action onReset)
		{
			if (onReset == null) return;
			l.Gap(4f);
			Rect row = l.GetRect(30f);
			float resetW = Mathf.Min(250f, row.width);
			if (resetW < 120f) resetW = Mathf.Max(100f, row.width * 0.35f);
			Rect resetRect = new Rect(row.x, row.y, resetW, row.height);
			Color prev = GUI.color;
			GUI.color = new Color(1f, 0.5f, 0.5f);
			if (Widgets.ButtonText(resetRect, "Restore defaults")) onReset();
			GUI.color = prev;
			l.Gap(6f);
		}

		private static float LabeledSlider(
			Listing_Standard l,
			string label,
			float val,
			float min,
			float max,
			string? tooltip = null,
			float step = 1f,
			SliderFormat format = SliderFormat.Percent,
			float? defaultValue = null)
		{
			l.Gap(2f);
			Rect r = l.GetRect(24f);
			if (!tooltip.NullOrEmpty())
			{
				TooltipHandler.TipRegion(r, TooltipWithDefault(tooltip, defaultValue, format));
			}

			Widgets.Label(r.LeftPart(0.5f), $"{label}: {FormatValue(val, format).Colorize(Color.cyan)}");
			return Widgets.HorizontalSlider(r.RightPart(0.5f), val, min, max, false, null, null, null, step);
		}

		private static string TooltipWithDefault(string? tooltip, float? defaultValue, SliderFormat format)
		{
			if (!defaultValue.HasValue) return tooltip ?? string.Empty;
			string line = $"Default: {FormatValue(defaultValue.Value, format)}";
			return tooltip.NullOrEmpty() ? line : tooltip + "\n" + line;
		}

		private static string FormatValue(float val, SliderFormat format) => format switch
		{
			SliderFormat.Fixed0 => val.ToString("F0"),
			SliderFormat.Percent => val.ToString("F0") + " %",
			_ => val.ToString()
		};
	}

	internal static class OpportunisticReloadThresholdApplier
	{
		internal static void TryApplyDefault(CompAmmoUser? ammo)
		{
			if (ammo == null)
			{
				return;
			}

			// Never touch CompAmmoUser getters during save/load — other MagSize patches
			// (e.g. Holder/Wielder/PrimaryVerb) can NRE while the gun is still wiring up.
			if (Scribe.mode != LoadSaveMode.Inactive)
			{
				return;
			}

			// One-off default only — leave player-tuned (or previously set) values alone.
			// Check before HasMagazine/MagSize so load-time / exotic MagSize postfixes are avoided.
			if (ammo.TryReloadOn != 0)
			{
				return;
			}

			try
			{
				if (!ammo.HasMagazine)
				{
					return;
				}

				int magSize = ammo.MagSize;
				if (magSize <= 1)
				{
					return;
				}

				float fraction = OpportunisticReload_Settings.Current?.ThresholdFraction ?? 0.8f;
				// Match CE's gizmo slider: Floor(fraction * MagSize).
				ammo.TryReloadOn = Mathf.FloorToInt(fraction * magSize);
			}
			catch (Exception)
			{
				// Soft-fail if another mod's MagSize patch blows up mid-init.
			}
		}
	}

	/// <summary>
	/// Colony pawns: set opportunistic reload threshold when equipping a CE magazine weapon.
	/// </summary>
	[HarmonyPatch(typeof(Pawn_EquipmentTracker), nameof(Pawn_EquipmentTracker.Notify_EquipmentAdded))]
	public static class Patch_Notify_EquipmentAdded
	{
		static void Postfix(Pawn_EquipmentTracker __instance, ThingWithComps eq)
		{
			Pawn pawn = __instance.pawn;
			if (pawn?.Faction == null || !pawn.Faction.IsPlayer)
			{
				return;
			}

			OpportunisticReloadThresholdApplier.TryApplyDefault(eq?.TryGetComp<CompAmmoUser>());
		}
	}

	/// <summary>
	/// Player CE turrets (manned Vickers/PKM/M240-style and unmanned CE turrets): set threshold when the gun is wired up.
	/// </summary>
	[HarmonyPatch(typeof(Building_TurretGunCE), "InitGun")]
	public static class Patch_Building_TurretGunCE_InitGun
	{
		static void Postfix(Building_TurretGunCE __instance)
		{
			if (__instance?.Faction == null || !__instance.Faction.IsPlayer)
			{
				return;
			}

			CompAmmoUser? ammo = __instance.CompAmmo;
			if (ammo == null && __instance.Gun is ThingWithComps gun)
			{
				ammo = gun.TryGetComp<CompAmmoUser>();
			}

			OpportunisticReloadThresholdApplier.TryApplyDefault(ammo);
		}
	}
}
