using ExileCore2;
using ExileCore2.PoEMemory;
using ExileCore2.PoEMemory.Elements;
using ExileCore2.PoEMemory.Elements.InventoryElements;
using ExileCore2.Shared.Enums;
using NinjaPricer.Enums;
using System;
using System.Collections.Generic;
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

    private List<NormalInventoryItem> GetInventoryItems()
    {
        var inventory = GameController.Game.IngameState.IngameUi.InventoryPanel;
        return !inventory.IsVisible ? null : inventory[InventoryIndex.PlayerInventory].VisibleInventoryItems.ToList();
    }

    private static List<CustomItem> FormatItems(IEnumerable<NormalInventoryItem> itemList)
    {
        return itemList.Where(x => x?.Item?.IsValid == true).Select(inventoryItem => new CustomItem(inventoryItem)).ToList();
    }

    private static bool TryGetShardParent(string shardBaseName, out string shardParent)
    {
        return ShardMapping.TryGetValue(shardBaseName, out shardParent);
    }

    private void GetHoveredItem()
    {
        try
        {
            var uiHover = GameController.Game.IngameState.UIHover;
            if (uiHover.Address != 0 && uiHover.AsObject<HoverItemIcon>().ToolTipType != ToolTipType.ItemInChat)
            {
                var inventoryItemIcon = uiHover.AsObject<NormalInventoryItem>();
                var tooltip = inventoryItemIcon.Tooltip;
                var poeEntity = inventoryItemIcon.Item;
                if (tooltip != null && poeEntity.Address != 0 && poeEntity.IsValid)
                {
                    var item = inventoryItemIcon.Item;
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
                        var currencySearch = CollectedData.Currency?.LinesByName.GetValueOrDefault(pricedItem);
                        if (currencySearch != null)
                        {
                            item.PriceData.MinChaosValue = item.CurrencyInfo.StackSize * currencySearch.Value.Line.PrimaryValue * PrimaryPrice / pricedStack;
                            item.PriceData.ChangeInLast7Days = currencySearch.Value.Line.Sparkline?.TotalChange ?? 0;
                            item.PriceData.DetailsId = currencySearch.Value.Item.DetailsId;
                        }

                        break;
                    }
                    case ItemTypes.Catalyst:
                        var catalystSearch = CollectedData.Breach?.LinesByName.GetValueOrDefault(item.BaseName);
                        if (catalystSearch != null)
                        {
                            item.PriceData.MinChaosValue = item.CurrencyInfo.StackSize * catalystSearch.Value.Line.PrimaryValue * PrimaryPrice;
                            item.PriceData.ChangeInLast7Days = catalystSearch.Value.Line.Sparkline?.TotalChange ?? 0;
                            item.PriceData.DetailsId = catalystSearch.Value.Item.DetailsId;
                        }

                        break;
                    case ItemTypes.Delirium:
                        var distilledSearch = CollectedData.Delirium?.LinesByName.GetValueOrDefault(item.BaseName);
                        if (distilledSearch != null)
                        {
                            item.PriceData.MinChaosValue = item.CurrencyInfo.StackSize * distilledSearch.Value.Line.PrimaryValue * PrimaryPrice;
                            item.PriceData.ChangeInLast7Days = distilledSearch.Value.Line.Sparkline?.TotalChange ?? 0;
                            item.PriceData.DetailsId = distilledSearch.Value.Item.DetailsId;
                        }

                        break;
                    case ItemTypes.UncutGem:
                        var uncutGemSearch = CollectedData.UncutGems?.LinesByName.GetValueOrDefault(item.BaseName);
                        if (uncutGemSearch != null)
                        {
                            item.PriceData.MinChaosValue = item.CurrencyInfo.StackSize * uncutGemSearch.Value.Line.PrimaryValue * PrimaryPrice;
                            item.PriceData.ChangeInLast7Days = uncutGemSearch.Value.Line.Sparkline?.TotalChange ?? 0;
                            item.PriceData.DetailsId = uncutGemSearch.Value.Line.Id;
                        }

                        break;
                    case ItemTypes.Abyss:
                        var abyssSearch = CollectedData.Abyss?.LinesByName.GetValueOrDefault(item.BaseName);
                        if (abyssSearch != null)
                        {
                            item.PriceData.MinChaosValue = item.CurrencyInfo.StackSize * abyssSearch.Value.Line.PrimaryValue * PrimaryPrice;
                            item.PriceData.ChangeInLast7Days = abyssSearch.Value.Line.Sparkline?.TotalChange ?? 0;
                            item.PriceData.DetailsId = abyssSearch.Value.Item.DetailsId;
                        }

                        break;
                    case ItemTypes.Essence:
                        var essenceSearch = CollectedData.Essences?.LinesByName.GetValueOrDefault(item.BaseName);
                        if (essenceSearch != null)
                        {
                            item.PriceData.MinChaosValue = item.CurrencyInfo.StackSize * essenceSearch.Value.Line.PrimaryValue * PrimaryPrice;
                            item.PriceData.ChangeInLast7Days = essenceSearch.Value.Line.Sparkline?.TotalChange ?? 0;
                            item.PriceData.DetailsId = essenceSearch.Value.Item.DetailsId;
                        }

                        break;
                    case ItemTypes.Rune:
                        var runeSearch = CollectedData.Runes?.LinesByName.GetValueOrDefault(item.BaseName);
                        if (runeSearch != null)
                        {
                            item.PriceData.MinChaosValue = item.CurrencyInfo.StackSize * runeSearch.Value.Line.PrimaryValue * PrimaryPrice;
                            item.PriceData.ChangeInLast7Days = runeSearch.Value.Line.Sparkline?.TotalChange ?? 0;
                            item.PriceData.DetailsId = runeSearch.Value.Item.DetailsId;
                        }

                        break;
                    case ItemTypes.Expedition:
                        var expeditionSearch = CollectedData.Expedition?.LinesByName.GetValueOrDefault(item.BaseName);
                        if (expeditionSearch != null)
                        {
                            item.PriceData.MinChaosValue = item.CurrencyInfo.StackSize * expeditionSearch.Value.Line.PrimaryValue * PrimaryPrice;
                            item.PriceData.ChangeInLast7Days = expeditionSearch.Value.Line.Sparkline?.TotalChange ?? 0;
                            item.PriceData.DetailsId = expeditionSearch.Value.Item.DetailsId;
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
                            ItemTypes.Omen => CollectedData.Ritual,
                            _ => null
                        };
                        var search = overview?.LinesByName.GetValueOrDefault(item.BaseName);
                        if (search != null)
                        {
                            item.PriceData.MinChaosValue = item.CurrencyInfo.StackSize * search.Value.Line.PrimaryValue * PrimaryPrice;
                            item.PriceData.ChangeInLast7Days = search.Value.Line.Sparkline?.TotalChange ?? 0;
                            item.PriceData.DetailsId = search.Value.Item.DetailsId;
                        }
                        break;
                    }
                    case ItemTypes.Fragment:
                    {
                        var (pricedStack, pricedItem) = item.CurrencyInfo.IsShard && TryGetShardParent(item.BaseName, out var shardParent)
                            ? (item.CurrencyInfo.MaxStackSize > 0 ? item.CurrencyInfo.MaxStackSize : 20, shardParent)
                            : (1, item.BaseName);
                        var fragmentSearch = CollectedData.Fragments?.LinesByName.GetValueOrDefault(pricedItem);
                        if (fragmentSearch != null)
                        {
                            item.PriceData.MinChaosValue = item.CurrencyInfo.StackSize * fragmentSearch.Value.Line.PrimaryValue * PrimaryPrice / pricedStack;
                            item.PriceData.ChangeInLast7Days = fragmentSearch.Value.Line.Sparkline?.TotalChange ?? 0;
                            item.PriceData.DetailsId = fragmentSearch.Value.Item.DetailsId;
                        }

                        break;
                    }
                    case ItemTypes.UniqueAccessory:
                    {
                        var uniqueAccessorySearch = CollectedData.Accessories?.Lines
                            .Where(x => x.Name == item.UniqueName || item.UniqueNameCandidates.Contains(x.Name))
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
                        var uniqueArmourSearchLinks = CollectedData.Armour?.Lines
                            .Where(x => x.Name == item.UniqueName || item.UniqueNameCandidates.Contains(x.Name))
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
                        var uniqueFlaskSearch = CollectedData.Flasks?.Lines
                            .Where(x => x.Name == item.UniqueName || item.UniqueNameCandidates.Contains(x.Name))
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
                        var uniqueJewelSearch = CollectedData.Jewels?.Lines
                            .Where(x => x.Name == item.UniqueName || item.UniqueNameCandidates.Contains(x.Name))
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
                        var uniqueWeaponSearchLinks = CollectedData.Weapons?.Lines
                            .Where(x => x.Name == item.UniqueName || item.UniqueNameCandidates.Contains(x.Name))
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
            if (item.PriceData.MaxChaosValue == 0)
            {
                item.PriceData.MaxChaosValue = item.PriceData.MinChaosValue;
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
                    var uniqueArmourSearch = CollectedData.Armour?.Lines
                        .Where(x => x.BaseType == item.BaseName)
                        .ToList() ?? new List<StashLine>();
                    foreach (var result in uniqueArmourSearch)
                    {
                        item.PriceData.ItemBasePrices.Add(result.PrimaryValue * PrimaryPrice);
                    }
                    break;
                case ItemTypes.UniqueWeapon:
                    var uniqueWeaponSearch = CollectedData.Weapons?.Lines
                        .Where(x => x.BaseType == item.BaseName)
                        .ToList() ?? new List<StashLine>();
                    foreach (var result in uniqueWeaponSearch)
                    {
                        item.PriceData.ItemBasePrices.Add(result.PrimaryValue * PrimaryPrice);
                    }
                    break;
                case ItemTypes.UniqueAccessory:
                    var uniqueAccessorySearch = CollectedData.Accessories?.Lines
                        .Where(x => x.BaseType == item.BaseName)
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