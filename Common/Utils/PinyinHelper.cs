using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Terraria;
using Terraria.Localization;

namespace MagicStorage.Common.Utils {
	/// <summary>
	/// 提供拼音搜索功能的工具类
	/// </summary>
	public static class PinyinHelper {
		/// <summary>
		/// 缓存物品类型的拼音信息，避免重复计算
		/// 使用 ConcurrentDictionary 确保线程安全
		/// </summary>
		private static readonly ConcurrentDictionary<int, PinyinInfo> _pinyinCache = new();

		/// <summary>
		/// NPinyin.Core 程序集缓存
		/// </summary>
		private static Assembly _nPinyinAssembly;

		/// <summary>
		/// NPinyin 是否已加载的标志（通过反射设置，避免循环依赖）
		/// </summary>
		private static bool? _nPinyinLoaded;

		/// <summary>
		/// NPinyin.Pinyin 类型缓存（通过反射获取，避免 JIT 阶段解析）
		/// </summary>
		private static Type _pinyinType;

		/// <summary>
		/// GetPinyin 方法缓存
		/// </summary>
		private static MethodInfo _getPinyinMethod;

		/// <summary>
		/// GetInitials 方法缓存
		/// </summary>
		private static MethodInfo _getInitialsMethod;

		/// <summary>
		/// 用于同步初始化 NPinyin 类型和方法的锁对象
		/// </summary>
		private static readonly object _pinyinInitLock = new object();

		/// <summary>
		/// 检查当前游戏语言是否为简体中文
		/// </summary>
		/// <returns>如果是简体中文返回true，否则返回false</returns>
		public static bool IsSimplifiedChinese() {
			try {
				// 检查当前活动语言文化
				var culture = Language.ActiveCulture;
				return culture != null && culture.Name == "zh-Hans";
			} catch {
				// 如果检测失败，默认返回false（不启用拼音搜索）
				return false;
			}
		}

		/// <summary>
		/// 检查是否应该启用拼音搜索
		/// 仅在简体中文环境下启用，且 NPinyin 库已加载
		/// </summary>
		/// <returns>是否启用拼音搜索</returns>
		public static bool ShouldEnablePinyinSearch() {
			// 检查配置选项、语言设置和 NPinyin 库是否已加载
			if (!MagicStorageConfig.EnablePinyinSearch || !IsSimplifiedChinese())
				return false;

			// 确保 NPinyin 库已加载
			return IsNPinyinLoaded();
		}

		/// <summary>
		/// 物品的拼音信息
		/// </summary>
		public class PinyinInfo {
			/// <summary>
			/// 完整拼音（小写，无空格），如 "tiekuang"
			/// </summary>
			public string FullPinyin { get; set; } = string.Empty;

			/// <summary>
			/// 拼音首字母（小写），如 "tk"
			/// </summary>
			public string FirstLetters { get; set; } = string.Empty;
		}

		/// <summary>
		/// 获取物品的拼音信息（带缓存）
		/// </summary>
		/// <param name="item">物品实例</param>
		/// <returns>拼音信息</returns>
		public static PinyinInfo GetPinyinInfo(Item item) {
			if (item?.IsAir != false)
				return new PinyinInfo();

			// 在 lambda 外部获取物品名称，避免闭包捕获 item 对象
			// 对于同一个 item.type，item.Name 应该是稳定的
			string itemName = item.Name ?? string.Empty;
			int itemType = item.type;

			// 使用 GetOrAdd 确保线程安全，避免重复计算
			return _pinyinCache.GetOrAdd(itemType, _ => ConvertToPinyin(itemName));
		}

		/// <summary>
		/// 检查搜索文本是否匹配物品名称
		/// 支持中文、拼音全拼、拼音首字母三种匹配方式
		/// </summary>
		/// <param name="item">物品实例</param>
		/// <param name="searchText">搜索文本</param>
		/// <returns>是否匹配</returns>
		public static bool MatchesSearch(Item item, string searchText) {
			if (item?.IsAir != false || string.IsNullOrEmpty(searchText))
				return false;

			// 获取物品名称，如果为null则使用空字符串
			string itemName = item.Name ?? string.Empty;
			searchText = searchText.Trim();

			// 如果搜索文本为空（经过Trim后），直接返回false
			if (string.IsNullOrEmpty(searchText))
				return false;

			// 1. 直接中文匹配（原有功能，保持兼容）
			// 这是最快的匹配方式，优先检查
			// 如果中文匹配成功，直接返回，避免不必要的拼音转换
			if (!string.IsNullOrEmpty(itemName) && 
				itemName.Contains(searchText, StringComparison.OrdinalIgnoreCase))
				return true;

			// 2. 拼音匹配（仅在中文匹配失败时执行，避免不必要的拼音转换）
			var pinyinInfo = GetPinyinInfo(item);

			// 2.1 拼音全拼匹配（不区分大小写）
			if (!string.IsNullOrEmpty(pinyinInfo.FullPinyin) &&
				pinyinInfo.FullPinyin.Contains(searchText, StringComparison.OrdinalIgnoreCase))
				return true;

			// 2.2 拼音首字母匹配（不区分大小写）
			if (!string.IsNullOrEmpty(pinyinInfo.FirstLetters) &&
				pinyinInfo.FirstLetters.Contains(searchText, StringComparison.OrdinalIgnoreCase))
				return true;

			return false;
		}

		/// <summary>
		/// 将中文文本转换为拼音信息
		/// 使用反射调用 NPinyin 库，避免在 JIT 阶段解析类型
		/// </summary>
		/// <param name="text">中文文本</param>
		/// <returns>拼音信息</returns>
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static PinyinInfo ConvertToPinyin(string text) {
			if (string.IsNullOrEmpty(text))
				return new PinyinInfo();

			// 如果 NPinyin 未加载，直接返回空信息
			// 通过反射检查，避免循环依赖
			if (!IsNPinyinLoaded())
				return new PinyinInfo();

			// 延迟初始化 NPinyin 类型和方法（使用反射避免 JIT 阶段解析）
			// 使用双重检查锁定模式确保线程安全
			if (_pinyinType == null || _getPinyinMethod == null || _getInitialsMethod == null) {
				try {
					lock (_pinyinInitLock) {
						// 再次检查，避免重复初始化
						if (_pinyinType == null || _getPinyinMethod == null || _getInitialsMethod == null) {
							try {
								// 使用反射查找已加载的程序集，避免直接引用
								Assembly assembly = FindNPinyinAssembly();
								if (assembly == null)
									return new PinyinInfo();

								// 从已加载的程序集中获取类型
								// 添加额外的空值检查，防止 assembly 本身有问题
								Type pinyinType = null;
								try {
									pinyinType = assembly.GetType("NPinyin.Pinyin", throwOnError: false);
								} catch {
									// 如果 GetType 抛出异常，pinyinType 保持为 null
								}

								if (pinyinType == null)
									return new PinyinInfo();

								// 获取方法（完全避免使用 GetMethod，只使用 GetMethods 然后手动过滤）
								// 这样可以完全避免 AmbiguousMatchException
								MethodInfo[] allMethods = GetMethodsSafely(pinyinType);
								if (allMethods == null || allMethods.Length == 0)
									return new PinyinInfo();
								
								// 查找 GetPinyin(string) 和 GetInitials(string) 方法
								MethodInfo getPinyinMethod = FindMethod(allMethods, "GetPinyin");
								MethodInfo getInitialsMethod = FindMethod(allMethods, "GetInitials");
								
								if (getPinyinMethod == null || getInitialsMethod == null)
									return new PinyinInfo();

								// 所有检查通过后，才赋值给静态字段（原子性操作）
								_nPinyinAssembly = assembly;
								_pinyinType = pinyinType;
								_getPinyinMethod = getPinyinMethod;
								_getInitialsMethod = getInitialsMethod;
							} catch (Exception) {
								// 如果初始化过程中出现任何异常，返回空信息
								// 不记录异常，避免日志污染
								return new PinyinInfo();
							}
						}
					}
				} catch (Exception) {
					// 如果锁外出现异常，返回空信息
					return new PinyinInfo();
				}
			}

			// 再次检查方法是否已初始化（防止在锁外被设置为 null）
			// 使用局部变量保存引用，避免在检查和使用之间被其他线程修改
			MethodInfo getPinyin = _getPinyinMethod;
			MethodInfo getInitials = _getInitialsMethod;
			if (getPinyin == null || getInitials == null)
				return new PinyinInfo();

			try {
				// 使用反射调用 GetPinyin 方法（使用局部变量，确保线程安全）
				object fullPinyinResult = getPinyin.Invoke(null, new object[] { text });
				string fullPinyin = fullPinyinResult?.ToString() ?? string.Empty;
				// 移除空格，转换为小写
				fullPinyin = fullPinyin.Replace(" ", "").ToLowerInvariant();

				// 使用反射调用 GetInitials 方法（使用局部变量，确保线程安全）
				object initialsResult = getInitials.Invoke(null, new object[] { text });
				string firstLetters = initialsResult?.ToString() ?? string.Empty;
				firstLetters = firstLetters.ToLowerInvariant();

				return new PinyinInfo {
					FullPinyin = fullPinyin,
					FirstLetters = firstLetters
				};
			} catch {
				// 如果转换失败，返回空信息（不影响原有搜索功能）
				return new PinyinInfo();
			}
		}

		/// <summary>
		/// 安全地获取类型的所有方法（避免 AmbiguousMatchException）
		/// </summary>
		/// <param name="type">要查找方法的类型</param>
		/// <returns>方法数组，如果失败返回 null</returns>
		private static MethodInfo[] GetMethodsSafely(Type type) {
			if (type == null)
				return null;
			
			// 方式1：不使用 FlattenHierarchy
			try {
				MethodInfo[] methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static);
				if (methods != null && methods.Length > 0)
					return methods;
			} catch (AmbiguousMatchException) {
				// 如果失败，尝试方式2
			} catch {
				// 其他异常也尝试方式2
			}
			
			// 方式2：使用 FlattenHierarchy
			try {
				MethodInfo[] methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
				if (methods != null && methods.Length > 0)
					return methods;
			} catch {
				// 如果还是失败，返回 null
			}
			
			return null;
		}

		/// <summary>
		/// 从方法数组中查找指定名称的方法（接受 string 或 object 参数）
		/// </summary>
		/// <param name="methods">方法数组</param>
		/// <param name="methodName">方法名称</param>
		/// <returns>找到的方法，如果未找到返回 null</returns>
		private static MethodInfo FindMethod(MethodInfo[] methods, string methodName) {
			if (methods == null || string.IsNullOrEmpty(methodName))
				return null;
			
			Type stringType = typeof(string);
			
			foreach (MethodInfo method in methods) {
				if (method == null || !method.IsStatic)
					continue;
				
				// method.Name 通常不会抛出异常，不需要额外的 try-catch
				if (method.Name != methodName)
					continue;
				
				try {
					ParameterInfo[] parameters = method.GetParameters();
					if (parameters == null || parameters.Length != 1)
						continue;
					
					ParameterInfo param = parameters[0];
					if (param == null)
						continue;
					
					Type paramType = param.ParameterType;
					if (paramType == null)
						continue;
					
					// 支持 string 或 object 类型的参数
					if (paramType == stringType || paramType == typeof(object))
						return method;
				} catch {
					// 忽略单个方法的错误，继续查找
					continue;
				}
			}
			
			return null;
		}

		/// <summary>
		/// 检查 NPinyin 是否已加载（通过反射，避免循环依赖）
		/// </summary>
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static bool IsNPinyinLoaded() {
			if (_nPinyinLoaded.HasValue)
				return _nPinyinLoaded.Value;

			try {
				// 通过反射检查 CheckModBuildVersionBeforeJIT.nPinyinLoaded
				// 使用当前程序集名称，避免硬编码
				Assembly currentAssembly = Assembly.GetExecutingAssembly();
				string assemblyName = currentAssembly.GetName().Name;
				Type checkType = Type.GetType($"MagicStorage.CheckModBuildVersionBeforeJIT, {assemblyName}");
				if (checkType != null) {
					FieldInfo field = checkType.GetField("nPinyinLoaded", BindingFlags.Public | BindingFlags.Static);
					if (field != null) {
						object value = field.GetValue(null);
						if (value is bool loaded) {
							_nPinyinLoaded = loaded;
							return loaded;
						}
					}
				}
			} catch {
				// 如果反射失败，假设未加载（安全策略）
			}

			// 默认返回 false，确保不会因为反射失败而启用拼音搜索
			_nPinyinLoaded = false;
			return false;
		}

		/// <summary>
		/// 获取已加载的 NPinyin.Core 程序集
		/// 从 MagicStorageMod 获取，避免在 JIT 阶段触发程序集解析
		/// </summary>
		[MethodImpl(MethodImplOptions.NoInlining)]
		private static Assembly FindNPinyinAssembly() {
			try {
				// 通过反射从 MagicStorageMod 获取程序集引用
				Type modType = Type.GetType("MagicStorage.MagicStorageMod, MagicStorage");
				if (modType == null)
					return null;

				PropertyInfo prop = modType.GetProperty("NPinyinAssembly", BindingFlags.Public | BindingFlags.Static);
				if (prop == null)
					return null;

				object assemblyObj = prop.GetValue(null);
				if (assemblyObj == null)
					return null;

				return assemblyObj as Assembly;
			} catch (Exception) {
				// 如果反射失败，返回 null
				return null;
			}
		}

		/// <summary>
		/// 清空拼音缓存（用于内存管理）
		/// </summary>
		public static void ClearCache() {
			_pinyinCache.Clear();
		}

		/// <summary>
		/// 获取缓存大小（用于调试和监控）
		/// </summary>
		public static int CacheSize => _pinyinCache.Count;
	}
}

