﻿using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Microsoft.Extensions.Logging;
using Moonglade.Configuration.Abstraction;

namespace Moonglade.Configuration
{
    public class BlogConfig : IBlogConfig
    {
        private readonly ILogger<BlogConfig> _logger;

        private readonly IDbConnection _dbConnection;

        public GeneralSettings GeneralSettings { get; set; }

        public ContentSettings ContentSettings { get; set; }

        public NotificationSettings NotificationSettings { get; set; }

        public FeedSettings FeedSettings { get; set; }

        public WatermarkSettings WatermarkSettings { get; set; }

        public FriendLinksSettings FriendLinksSettings { get; set; }

        public AdvancedSettings AdvancedSettings { get; set; }

        public SecuritySettings SecuritySettings { get; set; }

        public CustomStyleSheetSettings CustomStyleSheetSettings { get; set; }

        private bool _hasInitialized;

        public BlogConfig(
            ILogger<BlogConfig> logger,
            IDbConnection dbConnection)
        {
            _dbConnection = dbConnection;
            _logger = logger;

            ContentSettings = new();
            GeneralSettings = new();
            NotificationSettings = new();
            FeedSettings = new();
            WatermarkSettings = new();
            FriendLinksSettings = new();
            AdvancedSettings = new();
            SecuritySettings = new();
            CustomStyleSheetSettings = new();

            Initialize();
        }

        private void Initialize()
        {
            if (_hasInitialized) return;

            var cfgDic = GetAllConfigurations();

            GeneralSettings = cfgDic[nameof(GeneralSettings)].FromJson<GeneralSettings>();
            ContentSettings = cfgDic[nameof(ContentSettings)].FromJson<ContentSettings>();
            NotificationSettings = cfgDic[nameof(NotificationSettings)].FromJson<NotificationSettings>();
            FeedSettings = cfgDic[nameof(FeedSettings)].FromJson<FeedSettings>();
            WatermarkSettings = cfgDic[nameof(WatermarkSettings)].FromJson<WatermarkSettings>();
            FriendLinksSettings = cfgDic[nameof(FriendLinksSettings)].FromJson<FriendLinksSettings>();
            AdvancedSettings = cfgDic[nameof(AdvancedSettings)].FromJson<AdvancedSettings>();
            SecuritySettings = cfgDic[nameof(SecuritySettings)].FromJson<SecuritySettings>();
            CustomStyleSheetSettings = cfgDic[nameof(CustomStyleSheetSettings)].FromJson<CustomStyleSheetSettings>();

            _hasInitialized = true;
        }

        public async Task SaveAsync<T>(T blogSettings) where T : BlogSettings
        {
            async Task SetConfiguration(string key, string value)
            {
                var sql = $"UPDATE {nameof(BlogConfiguration)} " +
                          $"SET {nameof(BlogConfiguration.CfgValue)} = @value, " +
                          $"{nameof(BlogConfiguration.LastModifiedTimeUtc)} = @lastModifiedTimeUtc " +
                          $"WHERE {nameof(BlogConfiguration.CfgKey)} = @key";

                await _dbConnection.ExecuteAsync(sql, new { key, value, lastModifiedTimeUtc = DateTime.UtcNow });
            }

            var json = blogSettings.ToJson();
            var task = SetConfiguration(typeof(T).Name, json);

            try
            {
                await task;
                Dirty();
            }
            catch (Exception e)
            {
                _logger.LogError(e, e.Message);
                throw;
            }
        }

        public async Task SaveAssetAsync(Guid assetId, string assetBase64)
        {
            if (assetId == Guid.Empty) throw new ArgumentOutOfRangeException(nameof(assetId));
            if (string.IsNullOrWhiteSpace(assetBase64)) throw new ArgumentNullException(nameof(assetBase64));

            try
            {
                var exists = await
                    _dbConnection.ExecuteScalarAsync<int>("SELECT TOP 1 1 FROM BlogAsset ba WHERE ba.Id = @assetId",
                        new { assetId });

                if (exists == 0)
                {
                    await _dbConnection.ExecuteAsync(
                        "INSERT INTO BlogAsset(Id, Base64Data, LastModifiedTimeUtc) VALUES (@assetId, @assetBase64, @utcNow)",
                        new
                        {
                            assetId,
                            assetBase64,
                            DateTime.UtcNow
                        });
                }
                else
                {
                    await _dbConnection.ExecuteAsync(
                        "UPDATE BlogAsset SET Base64Data = @assetBase64, LastModifiedTimeUtc = @utcNow WHERE Id = @assetId",
                        new
                        {
                            assetId,
                            assetBase64,
                            DateTime.UtcNow
                        });
                }
            }
            catch (Exception e)
            {
                _logger.LogError(e, e.Message);
                throw;
            }
        }

        public async Task<string> GetAssetDataAsync(Guid assetId)
        {
            try
            {
                var asset = await _dbConnection.QueryFirstOrDefaultAsync<BlogAsset>
                    ("SELECT TOP 1 * FROM BlogAsset ba WHERE ba.Id = @assetId", new { assetId });

                return asset?.Base64Data;
            }
            catch (Exception e)
            {
                _logger.LogError(e, e.Message);
                throw;
            }
        }

        protected void Dirty()
        {
            _hasInitialized = false;
        }

        private IDictionary<string, string> GetAllConfigurations()
        {
            try
            {
                var sql = $"SELECT {nameof(BlogConfiguration.CfgKey)}, " +
                          $"{nameof(BlogConfiguration.CfgValue)} " +
                          $"FROM {nameof(BlogConfiguration)}";

                var data = _dbConnection.Query<(string CfgKey, string CfgValue)>(sql);
                var dic = data.ToDictionary(c => c.CfgKey, c => c.CfgValue);
                return dic;
            }
            catch (Exception e)
            {
                _logger.LogCritical(e, $"Error {nameof(GetAllConfigurations)}");
                throw;
            }
        }
    }
}