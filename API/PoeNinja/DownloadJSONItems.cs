using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NinjaPricer.API.PoeNinja.Models;

namespace NinjaPricer.API.PoeNinja;

public class DataDownloader
{
    private const string BaseUrl = "https://poe.ninja";

    private static string GetExchangeLink(string league, string type)
        => $"{BaseUrl}/poe2/api/economy/exchange/current/overview?league={league}&type={type}";

    private static string GetStashLink(string league, string type)
        => $"{BaseUrl}/poe2/api/economy/stash/current/item/overview?league={league}&type={type}";

    private static readonly Dictionary<string, string> ExchangeCategoryMap = new()
    {
        { "Currency", "Currency" },
        { "Breach", "Breach" },
        { "Delirium", "Delirium" },
        { "Essences", "Essences" },
        { "Runes", "Runes" },
        { "Ritual", "Ritual" },
        { "Fragments", "Fragments" },
        { "UncutGems", "UncutGems" },
        { "Abyss", "Abyss" },
        { "Expedition", "Expedition" },
    };

    private static readonly Dictionary<string, string> StashCategoryMap = new()
    {
        { "Weapons", "UniqueWeapons" },
        { "Armour", "UniqueArmours" },
        { "Accessories", "UniqueAccessories" },
        { "Flasks", "UniqueFlasks" },
        { "Jewels", "UniqueJewels" },
        { "Maps", "UniqueMaps" },
    };

    private int _updating;

    public CollectiveApiData? CollectedData { get; set; }
    public Action<string>? log { get; set; }
    public NinjaPricerSettings? Settings { get; set; }
    public string DataDirectory { get; set; } = string.Empty;

    public void StartDataReload(string league, bool forceRefresh)
    {
        StartDataReload(league, forceRefresh, CancellationToken.None);
    }

    public void StartDataReload(string league, bool forceRefresh, CancellationToken cancellationToken)
    {
        var logger = log;
        logger?.Invoke($"Getting data for {league}");

        if (Interlocked.CompareExchange(ref _updating, 1, 0) != 0)
        {
            logger?.Invoke("Update is already in progress");
            return;
        }

        var settings = Settings;
        var dataDir = DataDirectory;
        if (string.IsNullOrWhiteSpace(league) || settings == null || string.IsNullOrWhiteSpace(dataDir))
        {
            logger?.Invoke("Data reload aborted: invalid configuration or missing settings.");
            Interlocked.Exchange(ref _updating, 0);
            return;
        }

        if (cancellationToken.IsCancellationRequested)
        {
            logger?.Invoke("Data reload cancelled.");
            Interlocked.Exchange(ref _updating, 0);
            return;
        }

        Task.Run(async () =>
        {
            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                logger?.Invoke("Gathering Data from Poe.Ninja.");

                var newData = new CollectiveApiData();
                var tryWebFirst = forceRefresh;
                var metadataPath = Path.Join(dataDir, league, "meta.json");
                if (!tryWebFirst && settings.DataSourceSettings.AutoReload)
                {
                    tryWebFirst = await IsLocalCacheStale(metadataPath, settings, logger, cancellationToken).ConfigureAwait(false);
                }

                foreach (var (key, type) in ExchangeCategoryMap)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    var fileName = $"{key}.json";
                    var url = GetExchangeLink(league, type);
                    var data = await LoadFromWebOrBackup<ExchangeOverview>(fileName, url, tryWebFirst, settings, logger, dataDir, league, cancellationToken)
                        .ConfigureAwait(false);
                    if (data != null)
                    {
                        SetExchangeProperty(newData, key, data);
                    }
                }

                foreach (var (key, type) in StashCategoryMap)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        return;
                    }

                    var fileName = $"{key}.json";
                    var url = GetStashLink(league, type);
                    var data = await LoadFromWebOrBackup<StashOverview>(fileName, url, tryWebFirst, settings, logger, dataDir, league, cancellationToken)
                        .ConfigureAwait(false);
                    if (data != null)
                    {
                        SetStashProperty(newData, key, data);
                    }
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

                newData.DivineToExaltedRate = newData.DivineToExaltedRateRaw;

                new FileInfo(metadataPath).Directory?.Create();
                await File.WriteAllTextAsync(metadataPath, JsonConvert.SerializeObject(new LeagueMetadata { LastLoadTime = DateTime.UtcNow }), cancellationToken)
                    .ConfigureAwait(false);

                logger?.Invoke("Finished Gathering Data from Poe.Ninja.");
                CollectedData = newData;
                logger?.Invoke("Updated CollectedData.");
            }
            catch (OperationCanceledException)
            {
                logger?.Invoke("Data reload cancelled.");
            }
            catch (Exception ex)
            {
                logger?.Invoke($"Data reload failed: {ex}");
            }
            finally
            {
                Interlocked.Exchange(ref _updating, 0);
            }
        }, cancellationToken);
    }

    private static void SetExchangeProperty(CollectiveApiData data, string name, ExchangeOverview value)
    {
        switch (name)
        {
            case "Currency": data.Currency = value; break;
            case "Breach": data.Breach = value; break;
            case "Delirium": data.Delirium = value; break;
            case "Essences": data.Essences = value; break;
            case "Runes": data.Runes = value; break;
            case "Ritual": data.Ritual = value; break;
            case "Fragments": data.Fragments = value; break;
            case "UncutGems": data.UncutGems = value; break;
            case "Abyss": data.Abyss = value; break;
            case "Expedition": data.Expedition = value; break;
        }
    }

    private static void SetStashProperty(CollectiveApiData data, string name, StashOverview value)
    {
        switch (name)
        {
            case "Weapons": data.Weapons = value; break;
            case "Armour": data.Armour = value; break;
            case "Accessories": data.Accessories = value; break;
            case "Flasks": data.Flasks = value; break;
            case "Jewels": data.Jewels = value; break;
            case "Maps": data.Maps = value; break;
        }
    }

    private static async Task<bool> IsLocalCacheStale(string metadataPath, NinjaPricerSettings settings, Action<string>? logger, CancellationToken token)
    {
        if (!File.Exists(metadataPath))
        {
            return true;
        }

        try
        {
            var metadata = JsonConvert.DeserializeObject<LeagueMetadata>(await File.ReadAllTextAsync(metadataPath, token).ConfigureAwait(false));
            return metadata == null || DateTime.UtcNow - metadata.LastLoadTime > TimeSpan.FromMinutes(settings.DataSourceSettings.ReloadPeriod);
        }
        catch (Exception ex)
        {
            if (settings.DebugSettings.EnableDebugLogging)
            {
                logger?.Invoke($"Metadata loading failed: {ex}");
            }

            return true;
        }
    }

    private static async Task<T?> LoadFromWebOrBackup<T>(
        string fileName,
        string url,
        bool tryWebFirst,
        NinjaPricerSettings settings,
        Action<string>? logger,
        string dataDir,
        string league,
        CancellationToken token)
        where T : class
    {
        var backupFile = Path.Join(dataDir, league, fileName);

        if (tryWebFirst && await LoadFromWeb<T>(fileName, url, backupFile, settings, logger, token).ConfigureAwait(false) is { } webData)
        {
            return webData;
        }

        if (await LoadFromBackup<T>(fileName, backupFile, settings, logger, token).ConfigureAwait(false) is { } backupData)
        {
            return backupData;
        }

        if (!tryWebFirst)
        {
            return await LoadFromWeb<T>(fileName, url, backupFile, settings, logger, token).ConfigureAwait(false);
        }

        return null;
    }

    private static async Task<T?> LoadFromWeb<T>(
        string fileName,
        string url,
        string backupFile,
        NinjaPricerSettings settings,
        Action<string>? logger,
        CancellationToken token)
        where T : class
    {
        try
        {
            token.ThrowIfCancellationRequested();

            if (settings.DebugSettings.EnableDebugLogging)
            {
                logger?.Invoke($"Downloading {fileName}");
            }

            var json = await Utils.DownloadFromUrl(url).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
            {
                return null;
            }

            var data = JsonConvert.DeserializeObject<T>(json);
            if (data == null)
            {
                return null;
            }

            if (settings.DebugSettings.EnableDebugLogging)
            {
                logger?.Invoke($"{fileName} downloaded");
            }

            try
            {
                new FileInfo(backupFile).Directory?.Create();
                await File.WriteAllTextAsync(backupFile, JsonConvert.SerializeObject(data, Formatting.Indented), token).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                var errorPath = backupFile + ".error";
                new FileInfo(errorPath).Directory?.Create();
                await File.WriteAllTextAsync(errorPath, ex.ToString(), token).ConfigureAwait(false);
                if (settings.DebugSettings.EnableDebugLogging)
                {
                    logger?.Invoke($"{fileName} save failed: {ex}");
                }
            }

            return data;
        }
        catch (OperationCanceledException)
        {
            logger?.Invoke($"{fileName} download cancelled");
            return null;
        }
        catch (Exception ex)
        {
            if (settings.DebugSettings.EnableDebugLogging)
            {
                logger?.Invoke($"{fileName} fresh data download failed: {ex}");
            }

            return null;
        }
    }

    private static async Task<T?> LoadFromBackup<T>(
        string fileName,
        string backupFile,
        NinjaPricerSettings settings,
        Action<string>? logger,
        CancellationToken token)
        where T : class
    {
        if (File.Exists(backupFile))
        {
            try
            {
                return JsonConvert.DeserializeObject<T>(await File.ReadAllTextAsync(backupFile, token).ConfigureAwait(false));
            }
            catch (Exception backupEx)
            {
                if (settings.DebugSettings.EnableDebugLogging)
                {
                    logger?.Invoke($"{fileName} backup data load failed: {backupEx}");
                }
            }
        }
        else if (settings.DebugSettings.EnableDebugLogging)
        {
            logger?.Invoke($"No backup for {fileName}");
        }

        return null;
    }

    private class LeagueMetadata
    {
        public DateTime LastLoadTime { get; set; }
    }
}
