using ExileCore2;
using ExileCore2.PoEMemory;
using ExileCore2.PoEMemory.Elements;
using ExileCore2.PoEMemory.Elements.InventoryElements;
using ExileCore2.Shared.Enums;
using NinjaPricer.API.PoeNinja;
using NinjaPricer.API.PoeNinja.Models;
using NinjaPricer.Enums;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;

namespace NinjaPricer;

public partial class NinjaPricer
{
    private CustomItem _inspectedItem;

    private static readonly Dictionary<string, string> ShardMapping = new()
    {
        { "Transmutation Shard", "Orb of Transmutation" },
        { "Alteration Shard", "Orb of Alteration" },
        { "Annulment Shard", "Orb of Annulment" },
        { "Exalted Shard", "Exalted Orb" },
        { "Mirror Shard", "Mirror of Kalandra" },
        { "Regal Shard", "Regal Orb" },
        { "Alchemy Shard", "Orb of Alchemy" },
        { "Chaos Shard", "Chaos Orb" },
        { "Ancient Shard", "Ancient Orb" },
        { "Engineer's Shard", "Engineer's Orb" },
        { "Harbinger's Shard", "Harbinger's Orb" },
        { "Horizon Shard", "Orb of Horizons" },
        { "Binding Shard", "Orb of Binding" },
        { "Scroll Fragment", "Scroll of Wisdom" },
        { "Ritual Splinter", "Ritual Vessel" },
        { "Crescent Splinter", "The Maven's Writ" },
        { "Timeless Vaal Splinter", "Timeless Vaal Emblem" },
        { "Timeless Templar Splinter", "Timeless Templar Emblem" },
        { "Timeless Eternal Empire Splinter", "Timeless Eternal Emblem" },
        { "Timeless Maraketh Splinter", "Timeless Maraketh Emblem" },
        { "Timeless Karui Splinter", "Timeless Karui Emblem" },
        { "Splinter of Xoph", "Xoph's Breachstone" },
        { "Splinter of Tul", "Tul's Breachstone" },
        { "Splinter of Esh", "Esh's Breachstone" },
        { "Splinter of Uul-Netol", "Uul-Netol's Breachstone" },
        { "Splinter of Chayula", "Chayula's Breachstone" },
        //{ "Simulacrum Splinter", "Simulacrum" },
        { "Chance Shard", "Orb of Chance" },
    };

    private double DivinePrice => _downloader.CollectedData?.DivineToExaltedRate ?? 0;

    private bool TryGetDivinePrice(out double divinePrice)
    {
        divinePrice = DivinePrice;
        return double.IsFinite(divinePrice) && divinePrice > 0;
    }

    private static double NormalizePriceValue(double value)
    {
        return double.IsFinite(value) && value > 0 ? value : 0;
    }

    private static bool TryGetExchangeLine(
        ExchangeOverview? overview,
        string? itemName,
        [NotNullWhen(true)] out ExchangeLine? line,
        [NotNullWhen(true)] out ExchangeItem? item)
    {
        line = null;
        item = null;

        if (overview?.LinesByName == null || string.IsNullOrEmpty(itemName))
        {
            return false;
        }

        if (!overview.LinesByName.TryGetValue(itemName, out var result) || result.Line == null)
        {
            return false;
        }

        line = result.Line;
        item = result.Item;
        return true;
    }

    private double ChaosPerExalt
    {
        get
        {
            try
            {
                var currency = CollectedData?.Currency;
                var primaryToExaltedRate = currency?.PrimaryToExaltedRate ?? 0;
                if (string.Equals(currency?.Core?.Primary, "chaos", StringComparison.OrdinalIgnoreCase))
                {
                    return primaryToExaltedRate > 0 ? 1d / primaryToExaltedRate : double.NaN;
                }

                if (!TryGetExchangeLine(currency, "Chaos Orb", out var chaosLine, out _))
                {
                    return double.NaN;
                }

                var chaosValueInExalts = NormalizePriceValue(chaosLine.PrimaryValue * primaryToExaltedRate);
                return chaosValueInExalts > 0 ? 1d / chaosValueInExalts : double.NaN;
            }
            catch
            {
                return double.NaN;
            }
        }
    }

    private string FormatExWithChaosFallback(double exValue)
    {
        exValue = NormalizePriceValue(exValue);
        var exText = exValue.FormatNumber(Settings.VisualPriceSettings.SignificantDigits.Value, 0);
        if (Settings.VisualPriceSettings.ShowChaosFallbackBelowOneEx && exValue > 0 && exValue < 1)
        {
            var chaosPerEx = ChaosPerExalt;
            if (double.IsFinite(chaosPerEx) && chaosPerEx > 0)
            {
                var chaos = exValue * chaosPerEx;
                var chaosText = chaos.FormatNumber(2, forceDecimals: true);
                return $"{exText}ex ({chaosText}c)";
            }
        }

        return $"{exText}ex";
    }

    private List<NormalInventoryItem> GetInventoryItems()
    {
        var inventory = GameController.Game.IngameState.IngameUi.InventoryPanel;
        return !inventory.IsVisible ? null : inventory[InventoryIndex.PlayerInventory].VisibleInventoryItems.ToList();
    }

    private static List<CustomItem> FormatItems(IEnumerable<NormalInventoryItem> itemList)
    {
        return itemList?.Where(x => x?.Item?.IsValid == true).Select(inventoryItem => new CustomItem(inventoryItem)).ToList() ?? [];
    }

    private static bool TryGetShardParent(string shardBaseName, out string shardParent)
    {
        if (!string.IsNullOrEmpty(shardBaseName) && ShardMapping.TryGetValue(shardBaseName, out var mappedShardParent))
        {
            shardParent = mappedShardParent;
            return true;
        }

        shardParent = string.Empty;
        return false;
    }

    private void GetHoveredItem()
    {
        try
        {
            var uiHover = GameController.Game.IngameState.UIHover;
            if (uiHover.Address == 0)
            {
                HoveredItemTooltipRect = null;
                return;
            }

            var hoverItemIcon = uiHover.AsObject<HoverItemIcon>();
            if (hoverItemIcon?.ToolTipType != ToolTipType.ItemInChat)
            {
                var inventoryItemIcon = uiHover.AsObject<NormalInventoryItem>();
                var tooltip = inventoryItemIcon?.Tooltip;
                var poeEntity = inventoryItemIcon?.Item;
                if (inventoryItemIcon != null && tooltip != null && poeEntity is { Address: not 0, IsValid: true })
                {
                    var item = poeEntity;
                    var baseItemType = GameController.Files.BaseItemTypes.Translate(item.Path);
                    if (baseItemType != null)
                    {
                        HoveredItem = new CustomItem(inventoryItemIcon);
                        if (Settings.DebugSettings.InspectHoverHotkey.PressedOnce())
                        {
                            _inspectedItem = HoveredItem;
                        }
                        if (HoveredItem.ItemType != ItemTypes.None)
                            GetValue(HoveredItem);
                    }
                }
            }

            HoveredItemTooltipRect = HoveredItem?.Element?.Tooltip?.GetClientRectCache;
        }
        catch (Exception ex)
        {
            DebugWindow.LogError($"Failed to get the hovered item: {ex}");
        }
    }

    private void GetValue(IEnumerable<CustomItem> items)
    {
        if (items == null)
        {
            return;
        }

        foreach (var customItem in items)
        {
            GetValue(customItem);
        }
    }

    private T GetValue<T>(T items) where T : IReadOnlyCollection<CustomItem>
    {
        if (items == null)
        {
            return default;
        }

        foreach (var customItem in items)
        {
            GetValue(customItem);
        }

        return items;
    }

    private StashOverview? GetMatchingUniqueData(CollectiveApiData? root, ItemTypes type)
    {
        if (root == null)
        {
            return null;
        }

        return type switch
        {
            ItemTypes.UniqueAccessory => root.Accessories,
            ItemTypes.UniqueArmour => root.Armour,
            ItemTypes.UniqueFlask => root.Flasks,
            ItemTypes.UniqueJewel => root.Jewels,
            ItemTypes.UniqueWeapon => root.Weapons,
            ItemTypes.UniqueCharm => root.Charms,
            ItemTypes.UniqueMap => root.Maps,
            ItemTypes.Relic => root.SanctumRelics,
            _ => null,
        };
    }

    private ExchangeOverview? GetMatchingExchangeData(CollectiveApiData? root, ItemTypes type)
    {
        if (root == null)
        {
            return null;
        }

        return type switch
        {
            ItemTypes.None => null,
            ItemTypes.Currency => root.Currency,
            ItemTypes.Essence => root.Essences,
            ItemTypes.Fragment => root.Fragments,
            ItemTypes.SkillGem => root.LineageSupportGems,
            ItemTypes.UncutGem => root.UncutGems,
            ItemTypes.Omen => root.Ritual,
            ItemTypes.Catalyst => root.Breach,
            ItemTypes.Delirium => root.Delirium,
            ItemTypes.Rune => root.Runes,
            ItemTypes.Ultimatum => root.SoulCores,
            ItemTypes.Idol => root.Idols,
            ItemTypes.Expedition => root.Expedition,
            ItemTypes.Abyss => root.Abyss,
            ItemTypes.Verisium => root.Verisium,
            _ => null,
        };
    }

    private void GetValue(CustomItem item)
    {
        if (item?.PriceData == null)
        {
            return;
        }

        item.BaseName ??= string.Empty;
        item.UniqueName ??= string.Empty;
        item.CurrencyInfo ??= new CustomItem.CurrencyData();
        var uniqueNameCandidates = item.UniqueNameCandidates ?? [];

        try
        {
            if (!Settings.ValuationDisablingSettings.IsValuationDisabled(item.ItemType))
            {
                switch (item.ItemType) // easier to get data for each item type and handle logic based on that
                {
                    case ItemTypes.Currency:
                    {
                        if (item.BaseName == "Exalted Orb")
                        {
                            item.PriceData.MinChaosValue = item.CurrencyInfo.StackSize;
                            break;
                        }

                        var (pricedStack, pricedItem) = item.CurrencyInfo.IsShard && TryGetShardParent(item.BaseName, out var shardParent)
                            ? (item.CurrencyInfo.MaxStackSize > 0 ? item.CurrencyInfo.MaxStackSize : 20, shardParent)
                            : (1, item.BaseName);
                        var currency = CollectedData?.Currency;
                        if (TryGetExchangeLine(currency, pricedItem, out var currencyLine, out var currencyItem))
                        {
                            item.PriceData.MinChaosValue = item.CurrencyInfo.StackSize * currencyLine.PrimaryValue * (currency?.PrimaryToExaltedRate ?? 0) / pricedStack;
                            item.PriceData.ChangeInLast7Days = currencyLine.Sparkline?.TotalChange ?? 0;
                            item.PriceData.DetailsId = currencyItem?.DetailsId ?? currencyLine.Id;
                        }

                        break;
                    }
                    case ItemTypes.Fragment:
                    {
                        var (pricedStack, pricedItem) = item.CurrencyInfo.IsShard && TryGetShardParent(item.BaseName, out var shardParent)
                            ? (item.CurrencyInfo.MaxStackSize > 0 ? item.CurrencyInfo.MaxStackSize : 20, shardParent)
                            : (1, item.BaseName);
                        var fragments = CollectedData?.Fragments;
                        if (TryGetExchangeLine(fragments, pricedItem, out var fragmentLine, out var fragmentItem))
                        {
                            item.PriceData.MinChaosValue = item.CurrencyInfo.StackSize * fragmentLine.PrimaryValue * (fragments?.PrimaryToExaltedRate ?? 0) / pricedStack;
                            item.PriceData.ChangeInLast7Days = fragmentLine.Sparkline?.TotalChange ?? 0;
                            item.PriceData.DetailsId = fragmentItem?.DetailsId ?? fragmentLine.Id;
                        }

                        break;
                    }
                    case var v when GetMatchingExchangeData(CollectedData, v) is { } data:
                        if (TryGetExchangeLine(data, item.BaseName, out var genericLine, out var genericItem))
                        {
                            item.PriceData.MinChaosValue = item.CurrencyInfo.StackSize * genericLine.PrimaryValue * data.PrimaryToExaltedRate;
                            item.PriceData.ChangeInLast7Days = genericLine.Sparkline?.TotalChange ?? 0;
                            item.PriceData.DetailsId = genericItem?.DetailsId ?? genericLine.Id;
                        }

                        break;
                    case var v when GetMatchingUniqueData(CollectedData, v) is { } stashData:
                    {
                        var matches = stashData.Lines?
                            .Where(x => x != null && (x.Name == item.UniqueName || uniqueNameCandidates.Contains(x.Name)))
                            .ToList() ?? [];

                        if (matches.Count == 1)
                        {
                            item.PriceData.MinChaosValue = matches[0].PrimaryValue * stashData.PrimaryToExaltedRate;
                            item.PriceData.ChangeInLast7Days = matches[0].Sparkline?.TotalChange ?? 0;
                            item.PriceData.DetailsId = matches[0].DetailsId;
                        }
                        else if (matches.Count > 1)
                        {
                            item.PriceData.MinChaosValue = matches.Min(x => x.PrimaryValue) * stashData.PrimaryToExaltedRate;
                            item.PriceData.MaxChaosValue = matches.Max(x => x.PrimaryValue) * stashData.PrimaryToExaltedRate;
                            item.PriceData.ChangeInLast7Days = 0;
                            item.PriceData.DetailsId = matches[0].DetailsId;
                        }
                        else
                        {
                            item.PriceData.MinChaosValue = 0;
                            item.PriceData.ChangeInLast7Days = 0;
                        }

                        break;
                    }
                }
            }
        }
        catch (Exception)
        {
            if (Settings.DebugSettings.EnableDebugLogging) { LogMessage($"{GetCurrentMethod()}.GetValue()", 5, Color.Red); }
        }
        finally
        {
            item.PriceData.MinChaosValue = NormalizePriceValue(item.PriceData.MinChaosValue);
            item.PriceData.ChangeInLast7Days = double.IsFinite(item.PriceData.ChangeInLast7Days) ? item.PriceData.ChangeInLast7Days : 0;
            if (item.PriceData.MaxChaosValue == 0)
            {
                item.PriceData.MaxChaosValue = item.PriceData.MinChaosValue;
            }
            else
            {
                item.PriceData.MaxChaosValue = Math.Max(item.PriceData.MinChaosValue, NormalizePriceValue(item.PriceData.MaxChaosValue));
            }
        }
    }

    private bool ShouldUpdateValues()
    {
        if (StashUpdateTimer.ElapsedMilliseconds > Settings.DataSourceSettings.ItemUpdatePeriodMs)
        {
            StashUpdateTimer.Restart();
            if (Settings.DebugSettings.EnableDebugLogging) { LogMessage($"{GetCurrentMethod()} ValueUpdateTimer.Restart()", 5, Color.DarkGray); }
        }
        else
        {
            return false;
        }
        // TODO: Get inventory items and not just stash tab items, this will be done at a later date
        try
        {
            if (!Settings.StashValueSettings.Show)
            {
                if (Settings.DebugSettings.EnableDebugLogging) { LogMessage($"{GetCurrentMethod()}.ShouldUpdateValues() Stash is not visible", 5, Color.DarkGray); }
                return false;
            }
        }
        catch (Exception)
        {
            if (Settings.DebugSettings.EnableDebugLogging) LogMessage($"{GetCurrentMethod()}.ShouldUpdateValues()", 5, Color.DarkGray);
            return false;
        }

        if (Settings.DebugSettings.EnableDebugLogging) LogMessage($"{GetCurrentMethod()}.ShouldUpdateValues() == True", 5, Color.LimeGreen);
        return true;
    }

    private bool ShouldUpdateValuesInventory()
    {
        if (InventoryUpdateTimer.ElapsedMilliseconds > Settings.DataSourceSettings.ItemUpdatePeriodMs)
        {
            InventoryUpdateTimer.Restart();
            if (Settings.DebugSettings.EnableDebugLogging) { LogMessage($"{GetCurrentMethod()} ValueUpdateTimer.Restart()", 5, Color.DarkGray); }
        }
        else
        {
            return false;
        }
        // TODO: Get inventory items and not just stash tab items, this will be done at a later date
        try
        {
            if (!Settings.InventoryValueSettings.Show.Value || !GameController.Game.IngameState.IngameUi.InventoryPanel.IsVisible)
            {
                if (Settings.DebugSettings.EnableDebugLogging) { LogMessage($"{GetCurrentMethod()}.ShouldUpdateValuesInventory() Inventory is not visible", 5, Color.DarkGray); }
                return false;
            }

            // Dont continue if the stash page isnt even open
            if (GameController.Game.IngameState.IngameUi.InventoryPanel[InventoryIndex.PlayerInventory].VisibleInventoryItems == null)
            {
                if (Settings.DebugSettings.EnableDebugLogging) LogMessage($"{GetCurrentMethod()}.ShouldUpdateValuesInventory() Items == null", 5, Color.DarkGray);
                return false;
            }
        }
        catch (Exception)
        {
            if (Settings.DebugSettings.EnableDebugLogging) LogMessage($"{GetCurrentMethod()}.ShouldUpdateValuesInventory()", 5, Color.DarkGray);
            return false;
        }

        if (Settings.DebugSettings.EnableDebugLogging) LogMessage($"{GetCurrentMethod()}.ShouldUpdateValuesInventory() == True", 5, Color.LimeGreen);
        return true;
    }
}