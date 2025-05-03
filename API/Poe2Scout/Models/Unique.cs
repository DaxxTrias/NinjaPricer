using System;
using System.Collections.Generic;

namespace NinjaPricer.API.Poe2Scout.Models;

public class Unique
{
    public class RootObject : IPaged<Item>
    {
        public Item[] items { get; set; }
        public int total { get; set; }
        public int pages { get; set; }
        public int currentPage { get; set; }
    }

    public class Item
    {
        public int id { get; set; }
        public int itemId {get; set;}
        // public int currencyCategory { get; set; }
        // public string apiId { get; set; }
        public string text { get; set; }
        public string name { get; set; }
        public string categoryApiId { get; set; }
        public string iconUrl { get; set; }
        public ItemMetadata itemMetadata { get; set; }
        public List<PriceLogs> priceLogs { get; set; }
        public float currentPrice { get; set; }
        
        public string type { get; set; }
        public bool? isChanceable { get; set; }
    }

    public class ItemMetadata
    {
        public string name { get; set; }
        public string base_type { get; set; }
        public string icon { get; set; }
        // public int stack_size { get; set; }
        // public int max_stack_size { get; set; }
        public string description { get; set; }
        // public string[] effect { get; set; }
        
        public int item_level { get; set; }
        public Dictionary<string, string> properties { get; set; }
        public List<string> implicit_mods { get; set; }
        public List<string> explicit_mods { get; set; }
        public string flavor_text { get; set; }
        public Dictionary<string, string> requirements { get; set; }
    }

    public class PriceLogs
    {
        public float price { get; set; }
        public DateTime time { get; set; }
        public int quantity { get; set; }
    }
}