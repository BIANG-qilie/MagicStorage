using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using NPinyin;
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
		/// 仅在简体中文环境下启用
		/// </summary>
		/// <returns>是否启用拼音搜索</returns>
		public static bool ShouldEnablePinyinSearch() {
			// 检查配置选项和语言设置
			return MagicStorageConfig.EnablePinyinSearch && IsSimplifiedChinese();
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

			// 使用 GetOrAdd 确保线程安全，避免重复计算
			return _pinyinCache.GetOrAdd(item.type, type => {
				// 获取物品名称，如果为null则使用空字符串
				string itemName = item.Name ?? string.Empty;
				return ConvertToPinyin(itemName);
			});
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
		/// </summary>
		/// <param name="text">中文文本</param>
		/// <returns>拼音信息</returns>
		private static PinyinInfo ConvertToPinyin(string text) {
			if (string.IsNullOrEmpty(text))
				return new PinyinInfo();

			try {
				// 使用NPinyin库转换
				string fullPinyin = Pinyin.GetPinyin(text);
				// 移除空格，转换为小写
				fullPinyin = fullPinyin.Replace(" ", "").ToLowerInvariant();

				// 获取拼音首字母
				string firstLetters = Pinyin.GetInitials(text);
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

