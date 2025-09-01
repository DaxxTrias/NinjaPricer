using System.Collections.Generic;
using NinjaPricer.API.Poe2Scout.Models;

namespace NinjaPricer.API.Poe2Scout;

public class CollectiveApiData
{
    public List<Currency.Item> Currency { get; set; }
    public List<Currency.Item> Breach { get; set; }
    public List<Unique.Item> Weapons { get; set; }
    public List<Unique.Item> Armour { get; set; }
    public List<Unique.Item> Accessories { get; set; }
    public List<Currency.Item> Delirium { get; set; }
    public List<Currency.Item> Essences { get; set; }
    public List<Currency.Item> Runes { get; set; }
    public List<Currency.Item> Ritual { get; set; }
    public List<Currency.Item> Ultimatums { get; set; }
    public List<Currency.Item> Fragments { get; set; }
    public List<Currency.Item> Talismans { get; set; }
    public List<Currency.Item> Expeditions { get; set; }
    public List<Currency.Item> Waystones { get; set; }
    public List<Currency.Item> VaultKeys { get; set; }
    public List<Currency.Item> Abyss { get; set; }
}