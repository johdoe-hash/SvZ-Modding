using HarmonyLib;
using UnityEngine;

namespace SvZModLoader
{
	// Characters whose model swap has a [laser] section fire lasers: their bullets (projectile = Bullet,
	// which the game resolves instantly) are hidden and a LaserBeam is drawn from the model's muzzle
	// bone to where the target is hit.
	[HarmonyPatch]
	internal static class ProjectilePatches
	{
		[HarmonyPatch(typeof(Arrow), MethodType.Constructor, new[] { typeof(Projectile.EProjectileType), typeof(Character), typeof(Character), typeof(float), typeof(Vector3) })]
		[HarmonyPostfix]
		private static void FireLaser(Character shooter, Character target, GameObject ___mArrow)
		{
			if (shooter == null || target == null || ___mArrow == null || shooter.controlledObject == null)
			{
				return;
			}
			var swap = shooter.controlledObject.GetComponent<ModelSwap>();
			if (swap == null || swap.Laser == null)
			{
				return;
			}
			foreach (Renderer renderer in ___mArrow.GetComponentsInChildren<Renderer>(true))
			{
				renderer.enabled = false;
			}
			Vector3 hit = target.controller != null && target.controller.autoPaperdoll != null
				? target.controller.autoPaperdoll.GetJointPosition("impact_target")
				: target.position;
			LaserBeam.Fire(swap.LaserOrigin.position, hit, swap.Laser);
		}
	}
}
