using System.Collections.Generic;
using NinjaPricer.API.Poe2Scout.Models;

namespace NinjaPricer.API.Poe2Scout;

public class CollectiveApiData
{
    public List<Unique.Item> Armour { get; set; }
    public List<Currency.Item> Currency { get; set; }
    public List<Unique.Item> Weapons { get; set; }
    public List<Currency.Item> Breach { get; set; }
    public List<Unique.Item> Accessories { get; set; }
    public List<Currency.Item> Ultimatum { get; set; }
    public List<Currency.Item> Delirium { get; set; }
    public List<Currency.Item> Essences { get; set; }
    public List<Currency.Item> Ritual { get; set; }
    public List<Currency.Item> Runes { get; set; }
}