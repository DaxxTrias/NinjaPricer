using System;
using System.Collections.Generic;
using System.Linq;
using ExileCore2.PoEMemory;
using ExileCore2.PoEMemory.Components;
using ExileCore2.PoEMemory.Elements.InventoryElements;
using ExileCore2.PoEMemory.FilesInMemory;
using ExileCore2.PoEMemory.MemoryObjects;
using ExileCore2.PoEMemory.Models;
using ExileCore2.Shared.Enums;
using NinjaPricer.Enums;

namespace NinjaPricer;

public class CustomItem
{
    public static NinjaPricer Core;
    public string BaseName;
    public string UniqueName;
    public readonly string ClassName;
    public readonly bool IsIdentified;
    public readonly bool IsCorrupted;
    public readonly bool IsWeapon;
    public readonly bool IsHovered;
    public Element Element;
    public readonly Entity Entity;
    public int ItemLevel;
    public readonly string Path;
    public readonly int Quality;
    public readonly int GemLevel;
    public string GemName;
    public readonly ItemRarity Rarity;
    public readonly int Sockets;
    public readonly List<string> UniqueNameCandidates;
    public ItemTypes ItemType;
    public readonly List<string> EnchantedStats;
    public readonly string CapturedMonsterName;

    public readonly uint EntityId;
    public MapData MapInfo { get; set; } =  new MapData();
    public CurrencyData CurrencyInfo { get; set; } =  new CurrencyData();
    public NinjaPricer.RelevantPriceData PriceData { get; set; } = new NinjaPricer.RelevantPriceData();

    public static void InitCustomItem(NinjaPricer core)
    {
        Core = core;
    }

    public class MapData
    {
        public bool IsMap;
        public int MapTier;
    }
    public class CurrencyData
    {
        public bool IsShard;
        public int StackSize = 1;
        public int MaxStackSize = 0;
    }

    public CustomItem()
    {
    }

    public CustomItem(NormalInventoryItem item) : this(item.Item, item)
    {
    }

    public CustomItem(BaseItemType baseItemType)
    {
        Path = baseItemType.Metadata;
        ClassName = baseItemType.ClassName ?? string.Empty;
        BaseName = baseItemType.BaseName ?? string.Empty;
        ComputeType(null);
    }

    public CustomItem(Entity itemEntity, Element element)
    {
        try
        {
            EntityId = itemEntity.Id;
            if (element != null && element.Address != 0)
                Element = element;

            Path = itemEntity.Path;
            Entity = itemEntity;
            var baseItemType = Core.GameController.Files.BaseItemTypes.Translate(itemEntity.Path);
            ClassName = baseItemType?.ClassName ?? string.Empty;
            BaseName = baseItemType?.BaseName ?? string.Empty;
            var weaponClass = new List<string>
            {
                "One Hand Mace",
                "Two Hand Mace",
                "One Hand Axe",
                "Two Hand Axe",
                "One Hand Sword",
                "Two Hand Sword",
                "Thrusting One Hand Sword",
                "Bow",
                "Claw",
                "Dagger",
                "Sceptre",
                "Staff",
                "Wand"
            };
            if (itemEntity.TryGetComponent<Quality>(out var quality))
            {
                Quality = quality.ItemQuality;
            }

            if (itemEntity.TryGetComponent<SkillGem>(out var skillGem))
            {
                GemLevel = skillGem.Level;
                GemName = skillGem.GemEffect?.Name;
            }

            if (itemEntity.TryGetComponent<Base>(out var @base))
            {
                IsCorrupted = @base.isCorrupted;
                ItemLevel = @base.CurrencyItemLevel;
            }

            if (itemEntity.TryGetComponent<Mods>(out var mods))
            {
                Rarity = mods.ItemRarity;
                IsIdentified = mods.Identified;
                ItemLevel = mods.ItemLevel;
                EnchantedStats = mods.EnchantedStats;
                UniqueName = mods.UniqueName?.Replace('\x2019', '\x27');
                if (!IsIdentified && Rarity == ItemRarity.Unique)
                {
                    var artPath = itemEntity.GetComponent<RenderItem>()?.ResourcePath;
                    if (artPath != null)
                    {
                        UniqueNameCandidates = (Core.UniqueArtMapping.GetValueOrDefault(artPath) ?? Enumerable.Empty<string>())
                            .Where(x => !x.StartsWith("Replica "))
                            .ToList();
                    }
                }
            }

            UniqueNameCandidates ??= [];

            if (itemEntity.TryGetComponent<Sockets>(out var sockets))
            {
                try
                {
                    Sockets = sockets.NumberOfSockets;
                }
                catch
                {
                }
            }

            if (weaponClass.Any(ClassName.Equals))
                IsWeapon = true;

            MapInfo.MapTier = itemEntity.TryGetComponent<Map>(out var map) ? map.Tier : 0;
            MapInfo.IsMap = MapInfo.MapTier > 0;

            if (itemEntity.TryGetComponent<Stack>(out var stack))
            {
                CurrencyInfo.StackSize = stack.Size;
                CurrencyInfo.MaxStackSize = stack.Info.MaxStackSize;
                if (BaseName.EndsWith(" Shard") || 
                    BaseName.EndsWith(" Fragment") ||
                    BaseName.EndsWith(" Splinter") ||
                    BaseName.StartsWith("Splinter of"))
                    CurrencyInfo.IsShard = true;
            }

            if (itemEntity.TryGetComponent<CapturedMonster>(out var capturedMonster))
            {
                CapturedMonsterName = capturedMonster.MonsterVariety?.MonsterName;
            }

            IsHovered = Core.GameController.Game.IngameState.UIHover.AsObject<NormalInventoryItem>().Address == Element?.Address;

            ComputeType(itemEntity);
        }
        catch (Exception exception)
        {
            if (Core.Settings.DebugSettings.EnableDebugLogging)
                Core.LogError($"Ninja Pricer.CustomItem Error:\n{exception}");
        }

    }

    private void ComputeType(Entity itemEntity)
    {
        // sort items into types to use correct json data later from poe.ninja
        // This might need tweaking since if this catches anything other than currency.
        if (ClassName == "StackableCurrency" &&
                 !BaseName.StartsWith("Crescent Splinter") &&
                 !BaseName.StartsWith("Simulacrum") &&
                 !BaseName.EndsWith("Delirium Orb") &&
                 !BaseName.Contains("Essence") &&
                 !BaseName.Contains("Rune") &&
                 !BaseName.EndsWith(" Oil") &&
                 !BaseName.Contains("Tattoo ") &&
                 !BaseName.StartsWith("Omen ") &&
                 !BaseName.EndsWith("Artifact") &&
                 !BaseName.Contains("Astragali") &&
                 !BaseName.Contains("Burial Medallion") &&
                 !BaseName.Contains("Scrap Metal") &&
                 !BaseName.Contains("Exotic Coinage") &&
                 !BaseName.Contains("Remnant of") &&
                 !BaseName.Contains("Timeless ") &&
                 BaseName != "Prophecy" &&
                 BaseName != "Charged Compass" &&
                 ClassName != "MapFragment" &&
                 !BaseName.EndsWith(" Fossil") &&
                 !BaseName.StartsWith("Splinter of ") &&
                 ClassName != "Incubator" &&
                 !BaseName.EndsWith(" Catalyst") &&
                 BaseName != "Valdo's Puzzle Box" &&
                 !Path.Contains("Metadata/Items/Currency/Distilled", StringComparison.Ordinal))
        {
            ItemType = ItemTypes.Currency;
        }
        else if (ClassName== "StackableCurrency" && Path.Contains("Metadata/Items/Currency/Distilled", StringComparison.Ordinal))
        {
            ItemType = ItemTypes.Delirium;
        }
        else if (BaseName.EndsWith(" Catalyst"))
        {
            ItemType = ItemTypes.Catalyst;
        }
        else if (BaseName.Contains("Astragali") || BaseName.Contains("Burial Medallion") || BaseName.Contains("Scrap Metal") || BaseName.Contains("Exotic Coinage"))     
        {
            ItemType = ItemTypes.Artifact;
        }
        else if (BaseName.StartsWith("Omen "))
        {
            ItemType = ItemTypes.Omen;
        }
        else if (Path.Contains("Metadata/Items/DivinationCards"))
        {
            ItemType = ItemTypes.DivinationCard;
        }
        else if (BaseName.Contains("Essence") || BaseName.Contains("Remnant of"))
        {
            ItemType = ItemTypes.Essence;
        }
        else if (BaseName.Contains("Rune") && ClassName == "SoulCore")
        {
            ItemType = ItemTypes.Rune;
        }
        else if (BaseName.Contains("Soul Core") && ClassName == "SoulCore")
        {
            ItemType = ItemTypes.Ultimatum;
        }
        else if (BaseName.Contains("Talisman") && ClassName == "SoulCore")
        {
            ItemType = ItemTypes.Talisman;
        }
        else if (BaseName.EndsWith("Artifact") && ClassName == "StackableCurrency" || 
                 BaseName.Contains("Coinage") && ClassName == "StackableCurrency")
        {
            ItemType = ItemTypes.Expedition;
        }
        else if (BaseName.Contains("Waystone") && ClassName == "Map" || 
                 BaseName.Contains("Tablet") && ClassName == "TowerAugmentation")
        {
            ItemType = ItemTypes.Waystone;
        }
        else if (BaseName.Contains("Key") && ClassName == "VaultKey")
        {
            ItemType = ItemTypes.VaultKey;
        }
        else if (ClassName == "MapFragment" || BaseName.Contains("Timeless ") || BaseName.StartsWith("Simulacrum") ||
                 ClassName == "StackableCurrency" && BaseName.EndsWith("Splinter") ||
                 BaseName.StartsWith("Crescent Splinter") ||
                 ClassName == "VaultKey" ||
                 BaseName == "Valdo's Puzzle Box")
        {
            ItemType = ItemTypes.Fragment;
        }
        else if (MapInfo.IsMap && Rarity != ItemRarity.Unique)
        {
            ItemType = ItemTypes.Map;
        }
        else if (ClassName is "Support Skill Gem" or "Active Skill Gem")
        {
            ItemType = ItemTypes.SkillGem;
        }
        else
        {
            switch (Rarity) // Unique information
            {
                case ItemRarity.Unique when ClassName is "Amulet" or "Ring" or "Belt":
                    ItemType = ItemTypes.UniqueAccessory;
                    break;
                case ItemRarity.Unique when itemEntity?.HasComponent<Armour>() == true || ClassName == "Quiver":
                    ItemType = ItemTypes.UniqueArmour;
                    break;
                case ItemRarity.Unique when itemEntity?.HasComponent<Flask>() == true:
                    ItemType = ItemTypes.UniqueFlask;
                    break;
                case ItemRarity.Unique when ClassName == "Jewel":
                    ItemType = ItemTypes.UniqueJewel;
                    break;
                case ItemRarity.Unique when itemEntity?.HasComponent<Weapon>() == true || ClassName == "Sceptre":
                    ItemType = ItemTypes.UniqueWeapon;
                    break;
            }
        }
    }

    public override string ToString()
    {
        return string.IsNullOrWhiteSpace(UniqueName) ? BaseName : UniqueName;
    }
}