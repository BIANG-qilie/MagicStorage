# 拼音搜索功能开发方案

## 1. 功能概述

为Magic Storage模组的搜索系统添加拼音搜索支持，允许中文用户通过以下方式搜索物品：

- **拼音全拼**：输入 "tiekuang" 可以搜索到 "铁矿"
- **拼音首字母**：输入 "tk" 可以搜索到 "铁矿"
- **混合搜索**：同时支持中文、拼音全拼和拼音首字母
- **向后兼容**：不影响现有的中文直接搜索功能
- **语言检测**：仅在简体中文环境下自动启用，其他语言环境下自动禁用，不影响性能

## 2. 技术选型

### 2.1 拼音库：NPinyin

**选择NPinyin库的原因：**
- 轻量级，纯C#实现
- 无需外部依赖，易于集成
- 性能良好，适合实时搜索场景
- 支持中文转拼音和获取拼音首字母

**集成方式：**
- 将NPinyin.Core.dll放入 `references` 文件夹（✅ 已完成）
- 在 `MagicStorage.csproj` 中添加程序集引用

### 2.2 架构设计

```
用户输入搜索文本
    ↓
StorageViewControls.ItemPassesTextFilter()
    ↓
PinyinHelper.MatchesSearch()
    ↓
检查：中文匹配 || 拼音全拼匹配 || 拼音首字母匹配
    ↓
返回匹配结果
```

## 3. 实现方案

### 3.1 文件结构

```
Common/Utils/
  └── PinyinHelper.cs          # 拼音转换工具类（新建）
Common/Threading/Refreshing/
  └── StorageViewControls.cs   # 修改搜索过滤逻辑
MagicStorageConfig.cs
  └── 添加拼音搜索配置选项
MagicStorage.csproj
  └── 添加NPinyin引用
```

### 3.2 核心实现

#### 3.2.1 创建拼音工具类 (`Common/Utils/PinyinHelper.cs`)

```csharp
using System;
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
        /// </summary>
        private static readonly Dictionary<int, PinyinInfo> _pinyinCache = new();

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

            if (_pinyinCache.TryGetValue(item.type, out var cached))
                return cached;

            var info = ConvertToPinyin(item.Name);
            _pinyinCache[item.type] = info;
            return info;
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

            string itemName = item.Name;
            searchText = searchText.Trim();

            // 1. 直接中文匹配（原有功能，保持兼容）
            if (itemName.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                return true;

            // 2. 拼音匹配
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
```

#### 3.2.2 修改搜索过滤逻辑 (`Common/Threading/Refreshing/StorageViewControls.cs`)

在 `ItemPassesTextFilter` 方法中修改物品名称匹配部分（约第329-332行）：

**原代码：**
```csharp
if (!string.IsNullOrEmpty(itemNameSearchText)) {
    if (!item.Name.Contains(itemNameSearchText, StringComparison.OrdinalIgnoreCase))
        return false;
}
```

**修改为：**
```csharp
if (!string.IsNullOrEmpty(itemNameSearchText)) {
    // 支持拼音搜索（仅在简体中文环境下启用）
    if (PinyinHelper.ShouldEnablePinyinSearch()) {
        if (!PinyinHelper.MatchesSearch(item, itemNameSearchText))
            return false;
    } else {
        // 原有逻辑：直接字符串匹配
        if (!item.Name.Contains(itemNameSearchText, StringComparison.OrdinalIgnoreCase))
            return false;
    }
}
```

**需要添加的using语句：**
```csharp
using MagicStorage.Common.Utils;
```

#### 3.2.3 添加配置选项 (`MagicStorageConfig.cs`)

在 `MagicStorageConfig` 类中添加配置项（约第76行，在General部分）：

```csharp
[Header($"$Mods.MagicStorage.Config.Headers.General")]
[DefaultValue(false)]
public bool itemDataDebug;  //Previously "allowItemDataDebug"

[DefaultValue(true)]
public bool canMovePanels;

[DefaultValue(true)]
public bool automatonRemembers;

[DefaultValue(true)]
public bool enablePinyinSearch;  // 新增：启用拼音搜索（仅在简体中文环境下生效）
```

添加对应的静态属性访问器：

```csharp
[JsonIgnore]
public static bool EnablePinyinSearch => Instance.enablePinyinSearch;
```

**注意**：配置选项默认启用，但实际功能仅在简体中文环境下生效。这样设计的好处是：
- 如果用户切换到简体中文，拼音搜索会自动启用
- 如果用户使用其他语言，即使配置为启用，也不会执行拼音转换（节省性能）
- 用户可以通过配置手动禁用拼音搜索

#### 3.2.4 添加项目引用 (`MagicStorage.csproj`)

在 `ItemGroup` 中添加NPinyin.Core引用（在SerousCommonLib引用之后）：

```xml
<ItemGroup>
  <Reference Include="SerousCommonLib">
    <HintPath>$(SerousCommonLibPath)</HintPath>
  </Reference>
  <Reference Include="NPinyin.Core">
    <HintPath>..\references\NPinyin.Core.dll</HintPath>
  </Reference>
</ItemGroup>
```

#### 3.2.5 添加本地化文本 (`Localization/zh-Hans.hjson`)

在配置部分添加拼音搜索相关的本地化文本：

```hjson
Config: {
    Headers: {
        General: 通用设置
    }
    EnablePinyinSearch: {
        Label: 启用拼音搜索
        Tooltip: 启用后，可以通过拼音全拼或拼音首字母搜索中文物品名称
    }
}
```

## 4. 性能优化

### 4.1 缓存策略

- **缓存键**：使用物品类型ID (`item.type`) 作为缓存键
- **缓存时机**：首次访问时计算并缓存
- **缓存清理**：在模组卸载时清空缓存（可选）

### 4.2 性能考虑

- **延迟计算**：只在需要时进行拼音转换
- **缓存命中**：相同物品类型的后续搜索直接使用缓存
- **异常处理**：转换失败时不影响原有搜索功能

### 4.3 内存管理

- 缓存大小与物品类型数量成正比
- 考虑添加缓存大小限制（可选，当前实现暂不限制）

## 5. 测试方案

### 5.1 单元测试

创建测试文件 `Tests/PinyinHelperTests.cs`（如果项目支持测试）：

```csharp
using NUnit.Framework;
using MagicStorage.Common.Utils;
using Terraria;

namespace MagicStorage.Tests {
    [TestFixture]
    public class PinyinHelperTests {
        [Test]
        public void TestPinyinFullMatch() {
            // 创建测试物品（需要模拟Item对象）
            // 测试拼音全拼匹配
            // 输入 "tiekuang" 应该匹配 "铁矿"
        }

        [Test]
        public void TestPinyinFirstLetters() {
            // 测试拼音首字母匹配
            // 输入 "tk" 应该匹配 "铁矿"
        }

        [Test]
        public void TestChineseMatch() {
            // 测试中文直接匹配（确保向后兼容）
            // 输入 "铁矿" 应该匹配 "铁矿"
        }

        [Test]
        public void TestMixedSearch() {
            // 测试混合搜索
            // 输入 "tie" 应该匹配 "铁矿"、"铁剑" 等
        }

        [Test]
        public void TestCaseInsensitive() {
            // 测试大小写不敏感
            // 输入 "TieKuang" 应该匹配 "铁矿"
        }

        [Test]
        public void TestCachePerformance() {
            // 测试缓存性能
            // 多次查询同一物品应该使用缓存
        }
    }
}
```

### 5.2 集成测试

#### 5.2.1 功能测试清单

| 测试项 | 输入 | 预期结果 | 优先级 |
|--------|------|----------|--------|
| 拼音全拼匹配 | "tiekuang" | 匹配"铁矿" | 高 |
| 拼音首字母匹配 | "tk" | 匹配"铁矿" | 高 |
| 中文直接匹配 | "铁矿" | 匹配"铁矿" | 高 |
| 部分拼音匹配 | "tie" | 匹配所有"铁"开头的物品 | 中 |
| 大小写不敏感 | "TieKuang" | 匹配"铁矿" | 中 |
| 混合输入 | "tie kuang" | 匹配"铁矿"（如果支持空格） | 低 |
| 空搜索 | "" | 显示所有物品 | 高 |
| 无结果搜索 | "xxxxx" | 显示空结果 | 高 |
| 英文物品 | "sword" | 正常匹配英文物品 | 高 |
| 特殊字符 | "@#test" | 不影响特殊前缀功能 | 高 |
| **语言检测** | **简体中文环境** | **拼音搜索启用** | **高** |
| **语言检测** | **非简体中文环境** | **拼音搜索禁用，使用原有搜索** | **高** |
| **语言切换** | **切换语言后** | **功能自动启用/禁用** | **中** |

#### 5.2.2 测试步骤

1. **基础功能测试**
   - 启动游戏，打开存储界面
   - 输入拼音全拼搜索中文物品
   - 输入拼音首字母搜索中文物品
   - 输入中文直接搜索（验证向后兼容）
   - 验证搜索结果正确性

2. **性能测试**
   - 创建包含大量物品的存储系统（1000+物品）
   - 测试搜索响应时间
   - 检查内存使用情况
   - 验证缓存机制有效性

3. **兼容性测试**
   - 测试与模组搜索（@前缀）的兼容性
   - 测试与工具提示搜索（#前缀）的兼容性
   - 测试与过滤选项的兼容性
   - 测试配置开关功能

4. **边界情况测试**
   - 测试空字符串
   - 测试特殊字符
   - 测试多音字处理
   - 测试英文物品名称
   - 测试混合中英文物品名称

5. **语言环境测试**
   - 在简体中文环境下测试拼音搜索功能
   - 切换到其他语言（如英文），验证拼音搜索自动禁用
   - 验证其他语言的搜索功能不受影响
   - 测试语言切换后功能自动启用/禁用

### 5.3 测试环境准备

1. **测试数据准备**
   - 准备包含各种中文物品的测试存档
   - 包含常见物品：铁矿、木材、武器、装备等

2. **测试工具**
   - 使用游戏内搜索功能
   - 记录搜索时间和结果
   - 使用性能分析工具（如Visual Studio Profiler）

## 6. 实施步骤

### 阶段1：环境准备（1天）

1. **获取NPinyin库** ✅ 已完成
   - ✅ 已下载 NPinyin.Core.dll (版本 3.0.0, 74.5 KB)
   - ✅ 已放入 `references` 文件夹
   - ✅ 已验证库文件完整性

2. **项目配置** ✅ 已完成
   - ✅ 已修改 `MagicStorage.csproj` 添加NPinyin.Core引用
   - ✅ 已验证项目可以正常编译（0错误，编译成功）

### 阶段2：核心功能开发（2-3天）

1. **创建拼音工具类** ✅ 已完成
   - ✅ 已创建 `Common/Utils/PinyinHelper.cs`
   - ✅ 已实现拼音转换功能（使用NPinyin库）
   - ✅ 已实现缓存机制（基于物品类型ID）
   - ✅ 已实现匹配逻辑（支持中文、拼音全拼、拼音首字母）
   - ⚠️ 注意：配置项检查暂时禁用，等后续步骤添加配置项后再启用

2. **集成到搜索系统** ✅ 已完成
   - ✅ 已修改 `StorageViewControls.cs` 集成拼音搜索逻辑
   - ✅ 已在 `MagicStorageConfig.cs` 中添加配置选项 `enablePinyinSearch`
   - ✅ 已添加静态属性访问器 `EnablePinyinSearch`
   - ✅ 已在 `zh-Hans.hjson` 中添加本地化文本
   - ✅ 已更新 `PinyinHelper.cs` 启用配置检查

3. **基础测试**
   - 编译验证
   - 基础功能验证

### 阶段3：测试与优化（2-3天）

1. **功能测试**
   - 执行测试清单中的所有测试项
   - 记录问题和bug

2. **性能优化**
   - 性能测试
   - 优化缓存策略
   - 优化匹配算法

3. **Bug修复**
   - 修复发现的问题
   - 回归测试

### 阶段4：文档与发布（1天）

1. **文档更新**
   - 更新README（如需要）
   - 更新changelog
   - 添加使用说明

2. **最终验证**
   - 完整功能验证
   - 性能验证
   - 兼容性验证

## 7. 注意事项

### 7.1 多音字处理

- NPinyin库会选择一个常用读音
- 如果用户输入的是非常用读音，可能无法匹配
- **解决方案**：可以考虑支持多个读音，但会增加复杂度

### 7.2 性能考虑

- 首次搜索时需要进行拼音转换，可能略微影响性能
- 缓存机制可以显著提升后续搜索速度
- 如果发现性能问题，可以考虑：
  - 限制缓存大小
  - 使用更高效的缓存策略
  - 预加载常用物品的拼音

### 7.3 内存管理

- 缓存会占用一定内存
- 对于包含大量物品的存储系统，缓存可能占用较多内存
- **可选优化**：添加缓存大小限制，使用LRU策略

### 7.4 向后兼容

- 必须确保不影响现有的搜索功能
- 中文直接搜索必须继续工作
- 英文搜索不受影响
- 特殊前缀搜索（@、#）不受影响
- **非简体中文环境下，功能完全禁用，不影响任何现有功能**

### 7.5 错误处理

- 拼音转换可能失败（如遇到不支持字符）
- 转换失败时应该回退到原有搜索逻辑
- 不应该因为拼音转换错误导致搜索功能完全失效
- 语言检测失败时应该禁用拼音搜索，使用原有搜索逻辑

### 7.6 语言检测

- 仅在简体中文（zh-Hans）环境下启用拼音搜索
- 其他语言环境下自动禁用，不影响性能
- 语言切换后功能自动启用/禁用，无需重启游戏
- 如果语言检测失败，默认禁用拼音搜索（安全策略）

## 8. 可选增强功能

### 8.1 模糊匹配

- 支持拼音的容错匹配
- 例如：输入 "tiekuang" 也能匹配 "铁矿石"

### 8.2 搜索提示

- 在搜索框中显示拼音提示
- 帮助用户了解如何输入拼音

### 8.3 高亮显示

- 在搜索结果中高亮匹配的部分
- 提升用户体验

### 8.4 搜索历史

- 记录拼音搜索历史
- 方便用户快速重复搜索

## 9. 风险评估

| 风险项 | 风险等级 | 影响 | 应对措施 |
|--------|----------|------|----------|
| NPinyin库兼容性问题 | 低 | 功能无法使用 | 准备备用方案（自实现） |
| 性能问题 | 中 | 搜索变慢 | 优化缓存，必要时限制缓存大小 |
| 多音字问题 | 中 | 部分搜索无法匹配 | 文档说明，考虑支持多读音 |
| 内存占用 | 低 | 内存使用增加 | 监控内存，必要时添加限制 |
| 向后兼容性 | 低 | 破坏现有功能 | 充分测试，保持原有逻辑 |

## 10. 时间估算

- **环境准备**：1天
- **核心功能开发**：2-3天
- **测试与优化**：2-3天
- **文档与发布**：1天
- **总计**：6-8天

## 11. 参考资料

- NPinyin库文档：https://www.nuget.org/packages/NPinyin.Core
- 中文拼音转换标准：GB/T 16159-2012
- Magic Storage搜索系统代码：
  - `Common/Threading/Refreshing/StorageViewControls.cs`
  - `UI/GUIs/StorageGUI.Refreshing.cs`

## 12. 更新日志

- 2025-12-17：初始方案创建
- 确认使用NPinyin库
- 完成详细实现方案

