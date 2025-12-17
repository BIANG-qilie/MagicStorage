using MagicStorage.CrossMod;
using MagicStorage.CrossMod.Control;
using MagicStorage.CrossMod.Storage;
using SerousCommonLib.API.Helpers;
using SerousCommonLib.API.ModCall;
using System;
using System.IO;
using System.Reflection;
using Terraria;
using Terraria.Localization;
using Terraria.ModLoader;

namespace MagicStorage {
	public class MagicStorageMod : Mod {
		public static MagicStorageMod Instance => ModContent.GetInstance<MagicStorageMod>();

		internal static bool UsingPrivateBeta { get; private set; }  //Make sure to add the "NETPLAY" define when setting this to true for beta builds! -- absoluteAquarian

		// Integration with ModHelpers
		public static string GithubUserName => "blushiemagic";
		public static string GithubProjectName => "MagicStorage";

		public static readonly Condition HasCampfire = new(Language.GetText("Mods.MagicStorage.CookedMarshmallowCondition"), () => CraftingGUI.Campfire);

		public UIOptionConfigurationManager optionsConfig;

		public MagicStorageMod() {
			PreJITFilter = new CheckModBuildVersionBeforeJIT();
			CheckModBuildVersionBeforeJIT.Mod = this;
		}

		internal const string build144Version = "2023.8";

		public override void Load()
		{
			UsingPrivateBeta = DisplayName.Contains("BETA");

			// Load localization first to ensure it's available
			LocalizationHelper.ForceLoadModHJsonLocalization(this);

			// Load NPinyin.Core.dll from embedded resources (after localization)
			LoadNPinyinAssembly();

			InterfaceHelper.Initialize();

			//Sorting options
			SortingOptionLoader.Load();

			//Filtering options
			FilteringOptionLoader.Load();
		}

		/// <summary>
		/// 已加载的 NPinyin.Core 程序集引用
		/// </summary>
		public static Assembly NPinyinAssembly { get; private set; }

		/// <summary>
		/// 从嵌入资源加载 NPinyin.Core.dll
		/// </summary>
		private void LoadNPinyinAssembly() {
			try {
				// 获取当前程序集
				Assembly assembly = Assembly.GetExecutingAssembly();
				string resourceName = "NPinyin.Core.dll";

				// 从嵌入资源读取 DLL
				using (Stream stream = assembly.GetManifestResourceStream(resourceName)) {
					if (stream == null) {
						Logger.Warn($"Could not find embedded resource: {resourceName}. Pinyin search will be disabled.");
						return;
					}

					// 读取 DLL 字节数组
					byte[] assemblyData = new byte[stream.Length];
					stream.Read(assemblyData, 0, assemblyData.Length);

					// 加载程序集并保存引用
					NPinyinAssembly = Assembly.Load(assemblyData);
					
					// 标记 NPinyin 已加载，允许 JIT ConvertToPinyin 方法
					CheckModBuildVersionBeforeJIT.nPinyinLoaded = true;
					
					Logger.Info("Successfully loaded NPinyin.Core.dll from embedded resources.");
					Logger.Info("[DEBUG] Pinyin search feature is now enabled.");
				}
			} catch (Exception ex) {
				// 如果加载失败，记录错误但不影响模组其他功能
				Logger.Warn($"Failed to load NPinyin.Core.dll: {ex.Message}. Pinyin search will be disabled.");
			}
		}

		public override void Unload()
		{
			StorageGUI.Unload();
			CraftingGUI.Unload();
			EnvironmentGUI.Unload();
			DecraftingGUI.Unload();

			EnvironmentModuleLoader.Unload();

			SortingOptionLoader.Unload();
			FilteringOptionLoader.Unload();

			optionsConfig = null;

			CheckModBuildVersionBeforeJIT.Mod = null;
			CheckModBuildVersionBeforeJIT.versionChecked = false;
		}

		public override void PostSetupContent() {
			if (!Main.dedServ) {
				optionsConfig = new();
				optionsConfig.Initialize();
			}

			SortingOptionLoader.InitializeOrder();
			FilteringOptionLoader.InitializeOrder();
			StorageUnitTierLoader.PostSetupContent();
			StorageTierModifierLoader.PostSetupContent();
		}

		public override void HandlePacket(BinaryReader reader, int whoAmI) {
			NetHelper.HandlePacket(reader, whoAmI);
		}

		public override object Call(params object[] args) => BaseCallFunction.Call(this, args);
	}
}
