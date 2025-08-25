using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using ExileCore2.PoEMemory.Elements;
using ExileCore2.PoEMemory.Elements.InventoryElements;
using ExileCore2.Shared.Enums;
using NinjaPricer.Enums;

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
        { "Simulacrum Splinter", "Simulacrum" },
        { "Chance Shard", "Orb of Chance" },
    };

    private double DivinePrice => _downloader.DivineValue ?? 0;

    private List<NormalInventoryItem> GetInventoryItems()
    {
        var inventory = GameController.Game.IngameState.IngameUi.InventoryPanel;
        return !inventory.IsVisible ? null : inventory[InventoryIndex.PlayerInventory].VisibleInventoryItems.ToList();
    }

    private static List<CustomItem> FormatItems(IEnumerable<NormalInventoryItem> itemList)
    {
        return itemList.ToList().Where(x => x?.Item?.IsValid == true).Select(inventoryItem => new CustomItem(inventoryItem)).ToList();
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
            if (uiHover.AsObject<HoverItemIcon>().ToolTipType != ToolTipType.ItemInChat)
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
        }
        catch
        {
            // ignored
            //LogError("Error in GetHoveredItem()", 10);
        }
    }

    private void GetValue(CustomItem item)
    {
        try
        {
            if(item.BaseName.Contains("Rogue's Marker"))
            {
                item.PriceData.MinChaosValue = 0;
            }
            else
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
                        var shardCurrencySearch = CollectedData.Currency.Find(x=>x.text==pricedItem);
                        if (shardCurrencySearch != null)
                        {
                            item.PriceData.MinChaosValue = item.CurrencyInfo.StackSize * shardCurrencySearch.currentPrice / pricedStack;
                            item.PriceData.ChangeInLast7Days = 0;
                            item.PriceData.DetailsId = shardCurrencySearch.id;
                        }

                        break;
                    }
                    case ItemTypes.Catalyst:
                        var catalystSearch = CollectedData.Breach.Find(x=>x.text==item.BaseName);
                        if (catalystSearch != null)
                        {
                            item.PriceData.MinChaosValue = item.CurrencyInfo.StackSize * catalystSearch.currentPrice;
                            item.PriceData.ChangeInLast7Days = 0;
                            item.PriceData.DetailsId = catalystSearch.id;
                        }

                        break;
                    case ItemTypes.DistilledDelirium:
                        var distilledSearch = CollectedData.Delirium.Find(x => x.text == item.BaseName);
                        if (distilledSearch != null)
                        {
                            item.PriceData.MinChaosValue = item.CurrencyInfo.StackSize * distilledSearch.currentPrice;
                            item.PriceData.ChangeInLast7Days = 0;
                            item.PriceData.DetailsId = distilledSearch.id;
                        }

                        break;
                    //case ItemTypes.DivinationCard:
                    //    var divinationSearch = CollectedData.DivinationCards.Lines.Find(x => x.Name == item.BaseName);
                    //    if (divinationSearch != null)
                    //    {
                    //        item.PriceData.MinChaosValue = item.CurrencyInfo.StackSize * divinationSearch.ChaosValue ?? 0;
                    //        item.PriceData.ChangeInLast7Days = divinationSearch.Sparkline.TotalChange ?? 0;
                    //        item.PriceData.DetailsId = divinationSearch.DetailsId;
                    //    }

                    //    break;
                    case ItemTypes.Essence:
                        var essenceSearch = CollectedData.Essences.Find(x => x.text == item.BaseName);
                        if (essenceSearch != null)
                        {
                            item.PriceData.MinChaosValue = item.CurrencyInfo.StackSize * essenceSearch.currentPrice;
                            item.PriceData.ChangeInLast7Days = 0;
                            item.PriceData.DetailsId = essenceSearch.id;
                        }

                        break;
                    case ItemTypes.Rune:
                        var runeSearch = CollectedData.Runes.Find(x => x.text == item.BaseName);
                        if (runeSearch != null)
                        {
                            item.PriceData.MinChaosValue = item.CurrencyInfo.StackSize * runeSearch.currentPrice;
                            item.PriceData.ChangeInLast7Days = 0;
                            item.PriceData.DetailsId = runeSearch.id;
                        }

                        break;
                    case ItemTypes.Omen:
                        var omenSearch = CollectedData.Ritual.Find(x => x.text == item.BaseName);
                        if (omenSearch != null)
                        {
                            item.PriceData.MinChaosValue = item.CurrencyInfo.StackSize * omenSearch.currentPrice;
                            item.PriceData.ChangeInLast7Days = 0;
                            item.PriceData.DetailsId = omenSearch.id;
                        }
                        break;
                    //case ItemTypes.Artifact:
                    //    var artifactSearch = CollectedData.Artifacts.Lines.Find(x => x.Name == item.BaseName);
                    //    if (artifactSearch != null)
                    //    {
                    //        item.PriceData.MinChaosValue = item.CurrencyInfo.StackSize * artifactSearch.ChaosValue ?? 0;
                    //        item.PriceData.ChangeInLast7Days = artifactSearch.Sparkline.TotalChange ?? 0;
                    //        item.PriceData.DetailsId = artifactSearch.DetailsId;
                    //    }

                    //    break;
                    //case ItemTypes.Fragment:
                    //{
                    //    var (pricedStack, pricedItem) = item.CurrencyInfo.IsShard && TryGetShardParent(item.BaseName, out var shardParent)
                    //        ? (item.CurrencyInfo.MaxStackSize > 0 ? item.CurrencyInfo.MaxStackSize : 20, shardParent)
                    //        : (1, item.BaseName);
                    //    var fragmentSearch = CollectedData.Fragments.Lines.Find(x => x.CurrencyTypeName == pricedItem);
                    //    if (fragmentSearch != null)
                    //    {
                    //        item.PriceData.MinChaosValue = item.CurrencyInfo.StackSize * (fragmentSearch.ChaosEquivalent ?? 0) / pricedStack;
                    //        item.PriceData.ChangeInLast7Days = fragmentSearch.ReceiveSparkLine.TotalChange ?? 0;
                    //        item.PriceData.DetailsId = fragmentSearch.DetailsId;
                    //    }

                    //    break;
                    //}
                    //case ItemTypes.SkillGem:
                    //    var displayText = !string.IsNullOrEmpty(item.GemName) ? item.GemName : item.BaseName;
                    //    var fittingGems = CollectedData.SkillGems.Lines
                    //       .Where(x => x.Name == displayText).ToList();
                    //    var gemSearch = MoreLinq.MoreEnumerable.MaxBy(fittingGems,
                    //        x => (x.GemLevel == item.GemLevel,
                    //              x.Corrupted == item.IsCorrupted,
                    //              x.GemQuality == item.Quality,
                    //              x.GemQuality == item.Quality switch { > 15 and < 21 => 20, var o => o },
                    //              x.GemQuality <= item.Quality,
                    //              x.GemLevel > item.GemLevel ? -x.GemLevel : 0,
                    //              x.GemLevel + x.GemQuality)).ToList();

                    //    if (gemSearch.Any())
                    //    {
                    //        var minValueRecord = gemSearch.MinBy(x => x.ChaosValue)!;
                    //        item.PriceData.MinChaosValue = minValueRecord.ChaosValue;
                    //        item.PriceData.ChangeInLast7Days = minValueRecord.Sparkline.Data?.Any() == true
                    //                                               ? minValueRecord.Sparkline.TotalChange
                    //                                               : minValueRecord.LowConfidenceSparkline.TotalChange;
                    //        item.PriceData.DetailsId = minValueRecord.DetailsId;
                    //    }

                    //    break;
                    case ItemTypes.UniqueAccessory:
                        var uniqueAccessorySearch = CollectedData.Accessories.FindAll(x =>
                            (x.name == item.UniqueName || item.UniqueNameCandidates.Contains(x.name)));
                        if (uniqueAccessorySearch.Count == 1)
                        {
                            item.PriceData.MinChaosValue = uniqueAccessorySearch[0].currentPrice;
                            item.PriceData.ChangeInLast7Days = 0;
                            item.PriceData.DetailsId = uniqueAccessorySearch[0].id;
                        }
                        else if (uniqueAccessorySearch.Count > 1)
                        {
                            item.PriceData.MinChaosValue = uniqueAccessorySearch.Min(x => x.currentPrice);
                            item.PriceData.MaxChaosValue = uniqueAccessorySearch.Max(x => x.currentPrice);
                            item.PriceData.ChangeInLast7Days = 0;
                            item.PriceData.DetailsId = uniqueAccessorySearch[0].id;
                        }
                        else
                        {
                            item.PriceData.MinChaosValue = 0;
                            item.PriceData.ChangeInLast7Days = 0;
                        }

                        break;
                    case ItemTypes.UniqueArmour:
                    {
                        var uniqueArmourSearchLinks = CollectedData.Armour
                            .Where(x => x.name == item.UniqueName || item.UniqueNameCandidates.Contains(x.name))
                            .ToList();

                        if (uniqueArmourSearchLinks.Count == 1)
                        {
                            item.PriceData.MinChaosValue = uniqueArmourSearchLinks[0].currentPrice;
                            item.PriceData.ChangeInLast7Days = 0;
                            item.PriceData.DetailsId = uniqueArmourSearchLinks[0].id;
                        }
                        else if (uniqueArmourSearchLinks.Count > 1)
                        {
                            item.PriceData.MinChaosValue = uniqueArmourSearchLinks.Min(x => x.currentPrice);
                            item.PriceData.MaxChaosValue = uniqueArmourSearchLinks.Max(x => x.currentPrice);
                            item.PriceData.ChangeInLast7Days = 0;
                            item.PriceData.DetailsId = uniqueArmourSearchLinks[0].id;
                        }
                        else
                        {
                            item.PriceData.MinChaosValue = 0;
                            item.PriceData.ChangeInLast7Days = 0;
                        }

                        break;
                    }
                    //case ItemTypes.UniqueFlask:
                    //    var uniqueFlaskSearch = CollectedData.UniqueFlasks.Lines.FindAll(x =>
                    //        (x.Name == item.UniqueName || item.UniqueNameCandidates.Contains(x.Name)) &&
                    //        !x.DetailsId.Contains("-relic"));
                    //    if (uniqueFlaskSearch.Count == 1)
                    //    {
                    //        item.PriceData.MinChaosValue = uniqueFlaskSearch[0].ChaosValue ?? 0;
                    //        item.PriceData.ChangeInLast7Days = uniqueFlaskSearch[0].Sparkline.TotalChange ?? 0;
                    //        item.PriceData.DetailsId = uniqueFlaskSearch[0].DetailsId;
                    //    }
                    //    else if (uniqueFlaskSearch.Count > 1)
                    //    {
                    //        item.PriceData.MinChaosValue = uniqueFlaskSearch.Min(x => x.ChaosValue) ?? 0;
                    //        item.PriceData.MaxChaosValue = uniqueFlaskSearch.Max(x => x.ChaosValue) ?? 0;
                    //        item.PriceData.ChangeInLast7Days = 0;
                    //        item.PriceData.DetailsId = uniqueFlaskSearch[0].DetailsId;
                    //    }
                    //    else
                    //    {
                    //        item.PriceData.MinChaosValue = 0;
                    //        item.PriceData.ChangeInLast7Days = 0;
                    //    }

                    //    break;
                    //case ItemTypes.UniqueJewel:
                    //    var uniqueJewelSearch = CollectedData.UniqueJewels.Lines.FindAll(x =>
                    //        (x.Name == item.UniqueName || item.UniqueNameCandidates.Contains(x.Name)) &&
                    //        !x.DetailsId.Contains("-relic"));
                    //    if (uniqueJewelSearch.Count == 1)
                    //    {
                    //        item.PriceData.MinChaosValue = uniqueJewelSearch[0].ChaosValue ?? 0;
                    //        item.PriceData.ChangeInLast7Days = uniqueJewelSearch[0].Sparkline.TotalChange ?? 0;
                    //        item.PriceData.DetailsId = uniqueJewelSearch[0].DetailsId;
                    //    }
                    //    else if (uniqueJewelSearch.Count > 1)
                    //    {
                    //        item.PriceData.MinChaosValue = uniqueJewelSearch.Min(x => x.ChaosValue) ?? 0;
                    //        item.PriceData.MaxChaosValue = uniqueJewelSearch.Max(x => x.ChaosValue) ?? 0;
                    //        item.PriceData.ChangeInLast7Days = 0;
                    //        item.PriceData.DetailsId = uniqueJewelSearch[0].DetailsId;
                    //    }
                    //    else
                    //    {
                    //        item.PriceData.MinChaosValue = 0;
                    //        item.PriceData.ChangeInLast7Days = 0;
                    //    }

                    //    break;
                    case ItemTypes.UniqueWeapon:
                    {
                        var uniqueArmourSearchLinks = CollectedData.Weapons
                            .Where(x => x.name == item.UniqueName || item.UniqueNameCandidates.Contains(x.name))
                            .ToList();
                        if (uniqueArmourSearchLinks.Count == 1)
                        {
                            item.PriceData.MinChaosValue = uniqueArmourSearchLinks[0].currentPrice;
                            item.PriceData.ChangeInLast7Days = 0;
                            item.PriceData.DetailsId = uniqueArmourSearchLinks[0].id;
                        }
                        else if (uniqueArmourSearchLinks.Count > 1)
                        {
                            item.PriceData.MinChaosValue = uniqueArmourSearchLinks.Min(x => x.currentPrice);
                            item.PriceData.MaxChaosValue = uniqueArmourSearchLinks.Max(x => x.currentPrice);
                            item.PriceData.ChangeInLast7Days = 0;
                            item.PriceData.DetailsId = uniqueArmourSearchLinks[0].id;
                        }
                        else
                        {
                            item.PriceData.MinChaosValue = 0;
                            item.PriceData.ChangeInLast7Days = 0;
                        }

                        break;
                    }
                    //case ItemTypes.Map:
                    //    var normalMapSearch = CollectedData.WhiteMaps.Lines.Find(x => item.MapInfo.MapTier == x.MapTier);

                    //    if (normalMapSearch != null)
                    //    {
                    //        item.PriceData.MinChaosValue = normalMapSearch.ChaosValue ?? 0;
                    //        item.PriceData.ChangeInLast7Days = normalMapSearch.Sparkline.TotalChange ?? 0;
                    //        item.PriceData.DetailsId = normalMapSearch.DetailsId;
                    //    }

                    //    break;
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
                    var uniqueArmourSearch = CollectedData.Armour.FindAll(x => x.type == item.BaseName && x.IsChanceable());
                    if (uniqueArmourSearch.Count > 0)
                    {
                        foreach (var result in uniqueArmourSearch)
                        {
                            item.PriceData.ItemBasePrices.Add(result.currentPrice);
                        }
                    }
                    break;
                case ItemTypes.UniqueWeapon:
                    var uniqueWeaponSearch = CollectedData.Weapons.FindAll(x => x.type == item.BaseName && x.IsChanceable());
                    if (uniqueWeaponSearch.Count > 0)
                    {
                        foreach (var result in uniqueWeaponSearch)
                        {
                            item.PriceData.ItemBasePrices.Add(result.currentPrice);
                        }
                    }
                    break;
                case ItemTypes.UniqueAccessory:
                    var uniqueAccessorySearch = CollectedData.Accessories.FindAll(x => x.type == item.BaseName && x.IsChanceable());
                    if (uniqueAccessorySearch.Count > 0)
                    {
                        foreach (var result in uniqueAccessorySearch)
                        {
                            item.PriceData.ItemBasePrices.Add(result.currentPrice);
                        }
                    }
                    break;
                //case ItemTypes.UniqueJewel:
                //    var uniqueJewelSearch = CollectedData.UniqueJewels.Lines.FindAll(x => x.DetailsId.Contains(item.BaseName.ToLower().Replace(" ", "-")) && x.IsChanceable());
                //    if (uniqueJewelSearch.Count > 0)
                //    {
                //        foreach (var result in uniqueJewelSearch)
                //        {
                //            item.PriceData.ItemBasePrices.Add((double)result.ChaosValue);
                //        }
                //    }
                //    break;
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