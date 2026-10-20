using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;

namespace SvZModLoader
{
	// Resources.Load(string, Type) is implemented natively in Unity 2017.4, so it can't be hooked
	// directly. Instead every method in the game's own assemblies that calls Resources.Load gets a
	// Harmony transpiler that swaps the call for the matching ModAssets.Load overload. Nothing on disk
	// changes; the patch is applied in memory at startup.
	internal static class ResourcesRedirect
	{
		private static readonly MethodInfo s_LoadPath = AccessTools.Method(typeof(ModAssets), nameof(ModAssets.Load), new[] { typeof(string) });
		private static readonly MethodInfo s_LoadPathType = AccessTools.Method(typeof(ModAssets), nameof(ModAssets.Load), new[] { typeof(string), typeof(Type) });
		private static readonly MethodInfo s_LoadGeneric = AccessTools.Method(typeof(ModAssets), nameof(ModAssets.LoadGeneric));

		private const BindingFlags kAll = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;

		public static int Install(Harmony harmony)
		{
			var transpiler = new HarmonyMethod(AccessTools.Method(typeof(ResourcesRedirect), nameof(Transpiler)));
			int patched = 0;
			foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
			{
				string name = assembly.GetName().Name;
				if (name != "Assembly-CSharp" && name != "Assembly-CSharp-firstpass")
				{
					continue;
				}
				foreach (Type type in Types(assembly))
				{
					foreach (MethodBase method in type.GetMethods(kAll).Cast<MethodBase>().Concat(type.GetConstructors(kAll).Cast<MethodBase>()))
					{
						if (method.IsAbstract || method.ContainsGenericParameters || !CallsResourcesLoad(method))
						{
							continue;
						}
						try
						{
							harmony.Patch(method, transpiler: transpiler);
							patched++;
						}
						catch (Exception e)
						{
							Plugin.Log.LogWarning($"Couldn't redirect Resources.Load in {type.FullName}.{method.Name}: {e.Message}");
						}
					}
				}
			}
			return patched;
		}

		private static IEnumerable<Type> Types(Assembly assembly)
		{
			try
			{
				return assembly.GetTypes();
			}
			catch (ReflectionTypeLoadException e)
			{
				return e.Types.Where(t => t != null);
			}
		}

		private static bool IsResourcesLoad(MethodBase method)
		{
			return method != null && method.DeclaringType == typeof(Resources) && method.Name == "Load";
		}

		// Cheap pre-check on the raw IL: look for a call instruction whose target is Resources.Load.
		private static bool CallsResourcesLoad(MethodBase method)
		{
			MethodBody body;
			try
			{
				body = method.GetMethodBody();
			}
			catch
			{
				return false;
			}
			byte[] il = body?.GetILAsByteArray();
			if (il == null)
			{
				return false;
			}
			Type[] typeArgs = method.DeclaringType.IsGenericType ? method.DeclaringType.GetGenericArguments() : null;
			Type[] methodArgs = method.IsGenericMethod ? method.GetGenericArguments() : null;
			for (int i = 0; i + 4 < il.Length; i++)
			{
				if (il[i] != 0x28) // call
				{
					continue;
				}
				try
				{
					if (IsResourcesLoad(method.Module.ResolveMethod(BitConverter.ToInt32(il, i + 1), typeArgs, methodArgs)))
					{
						return true;
					}
				}
				catch
				{
					// Not actually a call token (0x28 inside another operand); keep scanning.
				}
			}
			return false;
		}

		private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			foreach (CodeInstruction instruction in instructions)
			{
				if ((instruction.opcode == OpCodes.Call || instruction.opcode == OpCodes.Callvirt) && instruction.operand is MethodInfo target && IsResourcesLoad(target))
				{
					if (target.IsGenericMethod)
					{
						instruction.operand = s_LoadGeneric.MakeGenericMethod(target.GetGenericArguments());
					}
					else
					{
						instruction.operand = target.GetParameters().Length == 1 ? s_LoadPath : s_LoadPathType;
					}
				}
				yield return instruction;
			}
		}
	}
}
