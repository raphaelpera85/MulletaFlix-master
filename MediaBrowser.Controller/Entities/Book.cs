#nullable disable

#pragma warning disable CS1591

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;
using MulletaFlix.Data.Enums;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.MediaInfo;

namespace MediaBrowser.Controller.Entities
{
    [Common.RequiresSourceSerialisation]
    public class Book : BaseItem, IHasLookupInfo<BookInfo>, IHasSeries, IHasMediaSources
    {
        public Book()
        {
            this.RunTimeTicks = TimeSpan.TicksPerSecond;
        }

        [JsonIgnore]
        public override MediaType MediaType => MediaType.Book;

        public override bool SupportsPlayedStatus => true;

        public override bool SupportsPositionTicksResume => true;

        [JsonIgnore]
        public override bool SupportsPeople => true;

        [JsonIgnore]
        public string SeriesPresentationUniqueKey { get; set; }

        [JsonIgnore]
        public string SeriesName { get; set; }

        [JsonIgnore]
        public Guid SeriesId { get; set; }

        public string FindSeriesSortName()
        {
            return SeriesName;
        }

        public string FindSeriesName()
        {
            return SeriesName;
        }

        public string FindSeriesPresentationUniqueKey()
        {
            return SeriesPresentationUniqueKey;
        }

        public Guid FindSeriesId()
        {
            return SeriesId;
        }

        /// <inheritdoc />
        public override bool CanDownload()
        {
            return IsFileProtocol;
        }

        /// <inheritdoc />
        public override UnratedItem GetBlockUnratedType()
        {
            return UnratedItem.Book;
        }

        public BookInfo GetLookupInfo()
        {
            var info = GetItemLookupInfo<BookInfo>();

            if (string.IsNullOrEmpty(SeriesName))
            {
                info.SeriesName = GetParents().Select(i => i.Name).FirstOrDefault();
            }
            else
            {
                info.SeriesName = SeriesName;
            }

            return info;
        }

        public IReadOnlyList<MediaSourceInfo> GetMediaSources(bool enablePathSubstitution)
        {
            return
            [
                new MediaSourceInfo
                {
                    Id = Id.ToString("N", CultureInfo.InvariantCulture).TrimEnd('=').Replace('/', '_'),
                    Path = enablePathSubstitution ? GetMappedPath(this, Path, null) ?? Path : Path,
                    Protocol = MediaProtocol.File,
                    Container = System.IO.Path.GetExtension(Path)?.TrimStart('.') ?? string.Empty,
                    MediaStreams = [],
                    Name = Name,
                    IsRemote = false,
                    ETag = System.IO.Path.GetExtension(Path)?.TrimStart('.')?.ToUpperInvariant(),
                    RunTimeTicks = RunTimeTicks,
                    Type = MediaSourceType.Default,
                    SupportsTranscoding = false,
                    SupportsDirectStream = true,
                    SupportsDirectPlay = true,
                    SupportsProbing = true,
                    Size = 0
                }
            ];
        }

        public IReadOnlyList<MediaStream> GetMediaStreams()
        {
            return [];
        }
    }
}

