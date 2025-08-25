using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NinjaPricer.API.Poe2Scout.Models;

namespace NinjaPricer.API.Poe2Scout;

public class DataDownloader {
    private const string baseUrl = "https://poe2scout.com/api";

    private string getLink(string path, string league, int page) {
        return $"{baseUrl}/{path}?league={league}&page={page}&perPage=250";
    }
    
    private int _updating;
    public double? DivineValue { get; set; }
    public CollectiveApiData CollectedData { get; set; }

    private class LeagueMetadata
    {
        public DateTime LastLoadTime { get; set; }
    }

    public Action<string> log { get; set; }
    public NinjaPricerSettings Settings { get; set; }
    public string DataDirectory { get; set; }

    public void StartDataReload(string league, bool forceRefresh)
    {
        log($"Getting data for {league}");

        if (Interlocked.CompareExchange(ref _updating, 1, 0) != 0)
        {
            log("Update is already in progress");
            return;
        }

        Task.Run(async () =>
        {
            try
            {
                log("Gathering Data from Poe.Ninja.");

                var newData = new CollectiveApiData();
                var tryWebFirst = forceRefresh;
                var metadataPath = Path.Join(DataDirectory, league, "meta.json");
                if (!tryWebFirst && Settings.DataSourceSettings.AutoReload)
                {
                    tryWebFirst = await IsLocalCacheStale(metadataPath);
                }

                newData.Currency = await LoadData<Currency.Item, Currency.RootObject>("Currency.json", "items/currency/currency", league, tryWebFirst);
                newData.Breach = await LoadData<Currency.Item, Currency.RootObject>("Breach.json", "items/currency/breachcatalyst", league, tryWebFirst);
                newData.Weapons = await LoadData<Unique.Item, Unique.RootObject>("Weapons.json", "items/unique/weapon", league, tryWebFirst);
                newData.Armour = await LoadData<Unique.Item, Unique.RootObject>("Armour.json", "items/unique/armour", league, tryWebFirst);
                newData.Accessories = await LoadData<Unique.Item, Unique.RootObject>("Accessories.json", "items/unique/accessory", league, tryWebFirst);
                newData.Delirium = await LoadData<Currency.Item, Currency.RootObject>("Delirium.json", "items/currency/deliriuminstill", league, tryWebFirst);
                newData.Essences = await LoadData<Currency.Item, Currency.RootObject>("Essences.json", "items/currency/essences", league, tryWebFirst);
                newData.Runes = await LoadData<Currency.Item, Currency.RootObject>("Runes.json", "items/currency/runes", league, tryWebFirst);
                newData.Ritual = await LoadData<Currency.Item, Currency.RootObject>("Ritual.json", "items/currency/ritual", league, tryWebFirst);
                newData.Ultimatum = await LoadData<Currency.Item, Currency.RootObject>("Ultimatum.json", "items/currency/ultimatum", league, tryWebFirst);
                newData.Fragments = await LoadData<Currency.Item, Currency.RootObject>("Fragments.json", "items/currency/fragments", league, tryWebFirst);
                newData.Talismans = await LoadData<Currency.Item, Currency.RootObject>("Talismans.json", "items/currency/talismans", league, tryWebFirst);
                newData.Expedition = await LoadData<Currency.Item, Currency.RootObject>("Expedition.json", "items/currency/expedition", league, tryWebFirst);
                newData.Waystones = await LoadData<Currency.Item, Currency.RootObject>("Waystones.json", "items/currency/waystones", league, tryWebFirst);
                newData.VaultKeys = await LoadData<Currency.Item, Currency.RootObject>("VaultKeys.json", "items/currency/vaultkeys", league, tryWebFirst);

                new FileInfo(metadataPath).Directory?.Create();
                await File.WriteAllTextAsync(metadataPath, JsonConvert.SerializeObject(new LeagueMetadata { LastLoadTime = DateTime.UtcNow }));

                log("Finished Gathering Data from Poe.Ninja.");
                CollectedData = newData;
                DivineValue = CollectedData.Currency.Find(x => x.text == "Divine Orb")?.currentPrice;
                log("Updated CollectedData.");
            }
            finally
            {
                Interlocked.Exchange(ref _updating, 0);
            }
        });
    }


    private async Task<bool> IsLocalCacheStale(string metadataPath)
    {
        if (!File.Exists(metadataPath))
        {
            return true;
        }

        try
        {
            var metadata = JsonConvert.DeserializeObject<LeagueMetadata>(await File.ReadAllTextAsync(metadataPath));
            return DateTime.UtcNow - metadata.LastLoadTime > TimeSpan.FromMinutes(Settings.DataSourceSettings.ReloadPeriod);
        }
        catch (Exception ex)
        {
            if (Settings.DebugSettings.EnableDebugLogging)
            {
                log($"Metadata loading failed: {ex}");
            }

            return true;
        }
    }

    private async Task<List<TItem>> LoadData<TItem, TPaged>(string fileName, string category, string league, bool tryWebFirst) where TPaged : class, IPaged<TItem>
    {
        var backupFile = Path.Join(DataDirectory, league, fileName);
        if (tryWebFirst) {
            if (await LoadPagedDataFromWeb<TItem, TPaged>(fileName, category, league, backupFile) is {} data)
            {
                return data;
            }
        }

        if (await LoadDataFromBackup<TItem>(fileName, backupFile) is {} data2)
        {
            return data2;
        }

        if (!tryWebFirst)
        {
            return await LoadPagedDataFromWeb<TItem, TPaged>(fileName, category, league, backupFile);
        }

        return null;
    }

    private async Task<List<T>> LoadDataFromBackup<T>(string fileName, string backupFile)
    {
        if (File.Exists(backupFile))
        {
            try
            {
                var data = JsonConvert.DeserializeObject<List<T>>(await File.ReadAllTextAsync(backupFile));
                return data;
            }
            catch (Exception backupEx)
            {
                if (Settings.DebugSettings.EnableDebugLogging)
                {
                    log($"{fileName} backup data load failed: {backupEx}");
                }
            }
        }
        else if (Settings.DebugSettings.EnableDebugLogging)
        {
            log($"No backup for {fileName}");
        }

        return null;
    }

    private async Task<List<TItem>> LoadPagedDataFromWeb<TItem, TPaged>(string fileName, string url, string league, string backupFile) where TPaged: class, IPaged<TItem>
    {
        try
        {
            var items = new List<TItem>();
            var page = 1;
            TPaged d=null;
            do
            {
                if (Settings.DebugSettings.EnableDebugLogging)
                {
                    log($"Downloading {fileName} ({page}/{d?.pages.ToString() ?? "?"})");
                }
                d = JsonConvert.DeserializeObject<TPaged>(await Utils.DownloadFromUrl(getLink(url, league, page)));
                items.AddRange(d.items);
                page++;
            } while (d.currentPage < d.pages);

            if (Settings.DebugSettings.EnableDebugLogging)
            {
                log($"{fileName} downloaded");
            }

            try
            {
                new FileInfo(backupFile).Directory.Create();
                await File.WriteAllTextAsync(backupFile, JsonConvert.SerializeObject(items, Formatting.Indented));
            }
            catch (Exception ex)
            {
                var errorPath = backupFile + ".error";
                new FileInfo(errorPath).Directory.Create();
                await File.WriteAllTextAsync(errorPath, ex.ToString());
                if (Settings.DebugSettings.EnableDebugLogging)
                {
                    log($"{fileName} save failed: {ex}");
                }
            }

            return items;
        }
        catch (Exception ex)
        {
            if (Settings.DebugSettings.EnableDebugLogging)
            {
                log($"{fileName} fresh data download failed: {ex}");
            }

            return null;
        }
    }
}