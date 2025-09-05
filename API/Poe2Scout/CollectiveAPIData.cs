using System.Collections.Generic;
using NinjaPricer.API.Poe2Scout.Models;

namespace NinjaPricer.API.Poe2Scout;

public class CollectiveApiData
{
    public List<Currency.Item> Currency { get; set; } = new();
    public List<Currency.Item> Breach { get; set; } = new();
    public List<Unique.Item> Weapons { get; set; } = new();
    public List<Unique.Item> Armour { get; set; } = new();
    public List<Unique.Item> Accessories { get; set; } = new();
    public List<Currency.Item> Delirium { get; set; } = new();
    public List<Currency.Item> Essences { get; set; } = new();
    public List<Currency.Item> Runes { get; set; } = new();
    public List<Currency.Item> Ritual { get; set; } = new();
    public List<Currency.Item> Ultimatums { get; set; } = new();
    public List<Currency.Item> Fragments { get; set; } = new();
    public List<Currency.Item> Talismans { get; set; } = new();
    public List<Currency.Item> Expeditions { get; set; } = new();
    public List<Currency.Item> Waystones { get; set; } = new();
    public List<Currency.Item> VaultKeys { get; set; } = new();
    public List<Currency.Item> Abyss { get; set; } = new();
    public List<Currency.Item> UncutGems { get; set; } = new();
}