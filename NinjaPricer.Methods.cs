using ExileCore2;
using ExileCore2.PoEMemory;
using ExileCore2.PoEMemory.Elements;
using ExileCore2.PoEMemory.Elements.InventoryElements;
using ExileCore2.Shared.Enums;
using NinjaPricer.Enums;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using NinjaPricer.API.PoeNinja.Models;

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
    private double PrimaryPrice => _downloader.CollectedData?.PrimaryToExaltedRate ?? 0;

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
                if (string.Equals(CollectedData?.Currency?.Core?.Primary, "chaos", StringComparison.OrdinalIgnoreCase))
                {
                    return PrimaryPrice > 0 ? 1d / PrimaryPrice : double.NaN;
                }

                if (!TryGetExchangeLine(CollectedData?.Currency, "Chaos Orb", out var chaosLine, out _))
                {
                    return double.NaN;
                }

                var chaosValueInExalts = NormalizePriceValue(chaosLine.PrimaryValue * PrimaryPrice);
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
        if (string.IsNullOrEmpty(shardBaseName))
        {
            shardParent = string.Empty;
            return false;
        }

        if (ShardMapping.TryGetValue(shardBaseName, out var mappedShardParent))
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
        foreach (var customItem in items)
        {
            GetValue(customItem);
        }

        return items;
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
                        if (TryGetExchangeLine(CollectedData?.Currency, pricedItem, out var currencyLine, out var currencyItem))
                        {
                            item.PriceData.MinChaosValue = item.CurrencyInfo.StackSize * currencyLine.PrimaryValue * PrimaryPrice / pricedStack;
                            item.PriceData.ChangeInLast7Days = currencyLine.Sparkline?.TotalChange ?? 0;
                            item.PriceData.DetailsId = currencyItem?.DetailsId ?? currencyLine.Id;
                        }

                        break;
                    }
                    case ItemTypes.Catalyst:
                        if (TryGetExchangeLine(CollectedData?.Breach, item.BaseName, out var catalystLine, out var catalystItem))
                        {
                            item.PriceData.MinChaosValue = item.CurrencyInfo.StackSize * catalystLine.PrimaryValue * PrimaryPrice;
                            item.PriceData.ChangeInLast7Days = catalystLine.Sparkline?.TotalChange ?? 0;
                            item.PriceData.DetailsId = catalystItem?.DetailsId ?? catalystLine.Id;
                        }

                        break;
                    case ItemTypes.Delirium:
                        if (TryGetExchangeLine(CollectedData?.Delirium, item.BaseName, out var distilledLine, out var distilledItem))
                        {
                            item.PriceData.MinChaosValue = item.CurrencyInfo.StackSize * distilledLine.PrimaryValue * PrimaryPrice;
                            item.PriceData.ChangeInLast7Days = distilledLine.Sparkline?.TotalChange ?? 0;
                            item.PriceData.DetailsId = distilledItem?.DetailsId ?? distilledLine.Id;
                        }

                        break;
                    case ItemTypes.UncutGem:
                        if (TryGetExchangeLine(CollectedData?.UncutGems, item.BaseName, out var uncutGemLine, out _))
                        {
                            item.PriceData.MinChaosValue = item.CurrencyInfo.StackSize * uncutGemLine.PrimaryValue * PrimaryPrice;
                            item.PriceData.ChangeInLast7Days = uncutGemLine.Sparkline?.TotalChange ?? 0;
                            item.PriceData.DetailsId = uncutGemLine.Id;
                        }

                        break;
                    case ItemTypes.Abyss:
                        if (TryGetExchangeLine(CollectedData?.Abyss, item.BaseName, out var abyssLine, out var abyssItem))
                        {
                            item.PriceData.MinChaosValue = item.CurrencyInfo.StackSize * abyssLine.PrimaryValue * PrimaryPrice;
                            item.PriceData.ChangeInLast7Days = abyssLine.Sparkline?.TotalChange ?? 0;
                            item.PriceData.DetailsId = abyssItem?.DetailsId ?? abyssLine.Id;
                        }

                        break;
                    case ItemTypes.Essence:
                        if (TryGetExchangeLine(CollectedData?.Essences, item.BaseName, out var essenceLine, out var essenceItem))
                        {
                            item.PriceData.MinChaosValue = item.CurrencyInfo.StackSize * essenceLine.PrimaryValue * PrimaryPrice;
                            item.PriceData.ChangeInLast7Days = essenceLine.Sparkline?.TotalChange ?? 0;
                            item.PriceData.DetailsId = essenceItem?.DetailsId ?? essenceLine.Id;
                        }

                        break;
                    case ItemTypes.Rune:
                        if (TryGetExchangeLine(CollectedData?.Runes, item.BaseName, out var runeLine, out var runeItem))
                        {
                            item.PriceData.MinChaosValue = item.CurrencyInfo.StackSize * runeLine.PrimaryValue * PrimaryPrice;
                            item.PriceData.ChangeInLast7Days = runeLine.Sparkline?.TotalChange ?? 0;
                            item.PriceData.DetailsId = runeItem?.DetailsId ?? runeLine.Id;
                        }

                        break;
                    case ItemTypes.Expedition:
                        if (TryGetExchangeLine(CollectedData?.Expedition, item.BaseName, out var expeditionLine, out var expeditionItem))
                        {
                            item.PriceData.MinChaosValue = item.CurrencyInfo.StackSize * expeditionLine.PrimaryValue * PrimaryPrice;
                            item.PriceData.ChangeInLast7Days = expeditionLine.Sparkline?.TotalChange ?? 0;
                            item.PriceData.DetailsId = expeditionItem?.DetailsId ?? expeditionLine.Id;
                        }

                        break;
                    case ItemTypes.Omen:
                    case ItemTypes.Ultimatum:
                    case ItemTypes.Talisman:
                    case ItemTypes.Waystone:
                    case ItemTypes.VaultKey:
                    {
                        var overview = item.ItemType switch
                        {
                            ItemTypes.Omen => CollectedData?.Ritual,
                            _ => null
                        };
                        if (TryGetExchangeLine(overview, item.BaseName, out var overviewLine, out var overviewItem))
                        {
                            item.PriceData.MinChaosValue = item.CurrencyInfo.StackSize * overviewLine.PrimaryValue * PrimaryPrice;
                            item.PriceData.ChangeInLast7Days = overviewLine.Sparkline?.TotalChange ?? 0;
                            item.PriceData.DetailsId = overviewItem?.DetailsId ?? overviewLine.Id;
                        }
                        break;
                    }
                    case ItemTypes.Fragment:
                    {
                        var (pricedStack, pricedItem) = item.CurrencyInfo.IsShard && TryGetShardParent(item.BaseName, out var shardParent)
                            ? (item.CurrencyInfo.MaxStackSize > 0 ? item.CurrencyInfo.MaxStackSize : 20, shardParent)
                            : (1, item.BaseName);
                        if (TryGetExchangeLine(CollectedData?.Fragments, pricedItem, out var fragmentLine, out var fragmentItem))
                        {
                            item.PriceData.MinChaosValue = item.CurrencyInfo.StackSize * fragmentLine.PrimaryValue * PrimaryPrice / pricedStack;
                            item.PriceData.ChangeInLast7Days = fragmentLine.Sparkline?.TotalChange ?? 0;
                            item.PriceData.DetailsId = fragmentItem?.DetailsId ?? fragmentLine.Id;
                        }

                        break;
                    }
                    case ItemTypes.UniqueAccessory:
                    {
                        var uniqueAccessorySearch = CollectedData?.Accessories?.Lines?
                            .Where(x => x != null && (x.Name == item.UniqueName || uniqueNameCandidates.Contains(x.Name)))
                            .ToList() ?? [];
                        if (uniqueAccessorySearch.Count == 1)
                        {
                            item.PriceData.MinChaosValue = uniqueAccessorySearch[0].PrimaryValue * PrimaryPrice;
                            item.PriceData.ChangeInLast7Days = uniqueAccessorySearch[0].Sparkline?.TotalChange ?? 0;
                            item.PriceData.DetailsId = uniqueAccessorySearch[0].DetailsId;
                        }
                        else if (uniqueAccessorySearch.Count > 1)
                        {
                            item.PriceData.MinChaosValue = uniqueAccessorySearch.Min(x => x.PrimaryValue) * PrimaryPrice;
                            item.PriceData.MaxChaosValue = uniqueAccessorySearch.Max(x => x.PrimaryValue) * PrimaryPrice;
                            item.PriceData.ChangeInLast7Days = 0;
                            item.PriceData.DetailsId = uniqueAccessorySearch[0].DetailsId;
                        }
                        else
                        {
                            item.PriceData.MinChaosValue = 0;
                            item.PriceData.ChangeInLast7Days = 0;
                        }

                        break;
                    }
                    case ItemTypes.UniqueArmour:
                    {
                        var uniqueArmourSearchLinks = CollectedData?.Armour?.Lines?
                            .Where(x => x != null && (x.Name == item.UniqueName || uniqueNameCandidates.Contains(x.Name)))
                            .ToList() ?? [];

                        if (uniqueArmourSearchLinks.Count == 1)
                        {
                            item.PriceData.MinChaosValue = uniqueArmourSearchLinks[0].PrimaryValue * PrimaryPrice;
                            item.PriceData.ChangeInLast7Days = uniqueArmourSearchLinks[0].Sparkline?.TotalChange ?? 0;
                            item.PriceData.DetailsId = uniqueArmourSearchLinks[0].DetailsId;
                        }
                        else if (uniqueArmourSearchLinks.Count > 1)
                        {
                            item.PriceData.MinChaosValue = uniqueArmourSearchLinks.Min(x => x.PrimaryValue) * PrimaryPrice;
                            item.PriceData.MaxChaosValue = uniqueArmourSearchLinks.Max(x => x.PrimaryValue) * PrimaryPrice;
                            item.PriceData.ChangeInLast7Days = 0;
                            item.PriceData.DetailsId = uniqueArmourSearchLinks[0].DetailsId;
                        }
                        else
                        {
                            item.PriceData.MinChaosValue = 0;
                            item.PriceData.ChangeInLast7Days = 0;
                        }

                        break;
                    }
                    case ItemTypes.UniqueFlask:
                    {
                        var uniqueFlaskSearch = CollectedData?.Flasks?.Lines?
                            .Where(x => x != null && (x.Name == item.UniqueName || uniqueNameCandidates.Contains(x.Name)))
                            .ToList() ?? [];
                        if (uniqueFlaskSearch.Count == 1)
                        {
                            item.PriceData.MinChaosValue = uniqueFlaskSearch[0].PrimaryValue * PrimaryPrice;
                            item.PriceData.ChangeInLast7Days = uniqueFlaskSearch[0].Sparkline?.TotalChange ?? 0;
                            item.PriceData.DetailsId = uniqueFlaskSearch[0].DetailsId;
                        }
                        else if (uniqueFlaskSearch.Count > 1)
                        {
                            item.PriceData.MinChaosValue = uniqueFlaskSearch.Min(x => x.PrimaryValue) * PrimaryPrice;
                            item.PriceData.MaxChaosValue = uniqueFlaskSearch.Max(x => x.PrimaryValue) * PrimaryPrice;
                            item.PriceData.ChangeInLast7Days = 0;
                            item.PriceData.DetailsId = uniqueFlaskSearch[0].DetailsId;
                        }
                        else
                        {
                            item.PriceData.MinChaosValue = 0;
                            item.PriceData.ChangeInLast7Days = 0;
                        }

                        break;
                    }
                    case ItemTypes.UniqueJewel:
                    {
                        var uniqueJewelSearch = CollectedData?.Jewels?.Lines?
                            .Where(x => x != null && (x.Name == item.UniqueName || uniqueNameCandidates.Contains(x.Name)))
                            .ToList() ?? [];
                        if (uniqueJewelSearch.Count == 1)
                        {
                            item.PriceData.MinChaosValue = uniqueJewelSearch[0].PrimaryValue * PrimaryPrice;
                            item.PriceData.ChangeInLast7Days = uniqueJewelSearch[0].Sparkline?.TotalChange ?? 0;
                            item.PriceData.DetailsId = uniqueJewelSearch[0].DetailsId;
                        }
                        else if (uniqueJewelSearch.Count > 1)
                        {
                            item.PriceData.MinChaosValue = uniqueJewelSearch.Min(x => x.PrimaryValue) * PrimaryPrice;
                            item.PriceData.MaxChaosValue = uniqueJewelSearch.Max(x => x.PrimaryValue) * PrimaryPrice;
                            item.PriceData.ChangeInLast7Days = 0;
                            item.PriceData.DetailsId = uniqueJewelSearch[0].DetailsId;
                        }
                        else
                        {
                            item.PriceData.MinChaosValue = 0;
                            item.PriceData.ChangeInLast7Days = 0;
                        }

                        break;
                    }
                    case ItemTypes.UniqueWeapon:
                    {
                        var uniqueWeaponSearchLinks = CollectedData?.Weapons?.Lines?
                            .Where(x => x != null && (x.Name == item.UniqueName || uniqueNameCandidates.Contains(x.Name)))
                            .ToList() ?? [];
                        if (uniqueWeaponSearchLinks.Count == 1)
                        {
                            item.PriceData.MinChaosValue = uniqueWeaponSearchLinks[0].PrimaryValue * PrimaryPrice;
                            item.PriceData.ChangeInLast7Days = uniqueWeaponSearchLinks[0].Sparkline?.TotalChange ?? 0;
                            item.PriceData.DetailsId = uniqueWeaponSearchLinks[0].DetailsId;
                        }
                        else if (uniqueWeaponSearchLinks.Count > 1)
                        {
                            item.PriceData.MinChaosValue = uniqueWeaponSearchLinks.Min(x => x.PrimaryValue) * PrimaryPrice;
                            item.PriceData.MaxChaosValue = uniqueWeaponSearchLinks.Max(x => x.PrimaryValue) * PrimaryPrice;
                            item.PriceData.ChangeInLast7Days = 0;
                            item.PriceData.DetailsId = uniqueWeaponSearchLinks[0].DetailsId;
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
            item.PriceData.ItemBasePrices = item.PriceData.ItemBasePrices?
                .Where(double.IsFinite)
                .Where(x => x > 0)
                .ToList() ?? [];

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

    private void GetValueHaggle(CustomItem item)
    {
        try
        {
            switch (item.ItemType) // easier to get data for each item type and handle logic based on that
            {
                case ItemTypes.UniqueArmour:
                    var uniqueArmourSearch = CollectedData?.Armour?.Lines?
                        .Where(x => x != null && x.BaseType == item.BaseName)
                        .ToList() ?? new List<StashLine>();
                    foreach (var result in uniqueArmourSearch)
                    {
                        item.PriceData.ItemBasePrices.Add(result.PrimaryValue * PrimaryPrice);
                    }
                    break;
                case ItemTypes.UniqueWeapon:
                    var uniqueWeaponSearch = CollectedData?.Weapons?.Lines?
                        .Where(x => x != null && x.BaseType == item.BaseName)
                        .ToList() ?? new List<StashLine>();
                    foreach (var result in uniqueWeaponSearch)
                    {
                        item.PriceData.ItemBasePrices.Add(result.PrimaryValue * PrimaryPrice);
                    }
                    break;
                case ItemTypes.UniqueAccessory:
                    var uniqueAccessorySearch = CollectedData?.Accessories?.Lines?
                        .Where(x => x != null && x.BaseType == item.BaseName)
                        .ToList() ?? new List<StashLine>();
                    foreach (var result in uniqueAccessorySearch)
                    {
                        item.PriceData.ItemBasePrices.Add(result.PrimaryValue * PrimaryPrice);
                    }
                    break;
            }
        }
        catch (Exception e)
        {
            if (Settings.DebugSettings.EnableDebugLogging)
            {
                LogError($"{GetCurrentMethod()}.GetValueHaggle() Error that i dont understand, Item: {item.BaseName}: {e}");
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