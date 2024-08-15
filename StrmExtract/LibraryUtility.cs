using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.IO;
using MediaBrowser.Model.Logging;
using MediaBrowser.Model.Querying;
using System;
using System.Collections.Generic;
using System.Linq;

namespace StrmExtract
{
    public class LibraryUtility
    {
        private readonly ILibraryManager _libraryManager;
        private readonly IFileSystem _fileSystem;
        private readonly IUserManager _userManager;
        private readonly ILogger _logger;

        public static MetadataRefreshOptions MediaInfoRefreshOptions;
        public static ExtraType[] extraType = new ExtraType[] { ExtraType.AdditionalPart,
                                                                ExtraType.BehindTheScenes,
                                                                ExtraType.Clip,
                                                                ExtraType.DeletedScene,
                                                                ExtraType.Interview,
                                                                ExtraType.Sample,
                                                                ExtraType.Scene,
                                                                ExtraType.ThemeSong,
                                                                ExtraType.ThemeVideo,
                                                                ExtraType.Trailer };
        public static User[] allUsers;

        public LibraryUtility(ILibraryManager libraryManager,
            IFileSystem fileSystem,
            IUserManager userManager)
        {
            _libraryManager = libraryManager;
            _logger = Plugin.Instance.logger;
            _fileSystem = fileSystem;
            _userManager = userManager;

            FetchUsers();

            MediaInfoRefreshOptions = new MetadataRefreshOptions(_fileSystem)
            {
                EnableRemoteContentProbe = true,
                ReplaceAllMetadata = true,
                EnableThumbnailImageExtraction = false,
                ImageRefreshMode = MetadataRefreshMode.ValidationOnly,
                MetadataRefreshMode = MetadataRefreshMode.ValidationOnly,
                ReplaceAllImages = false
            };
        }

        public void FetchUsers()
        {
            UserQuery userQuery = new UserQuery();
            userQuery.IsDisabled = false;
            allUsers = _userManager.GetUserList(userQuery);
        }

        public List<BaseItem> FetchItems(List<BaseItem> items)
        {
            var movies = items?.OfType<Movie>().Cast<BaseItem>();

            InternalItemsQuery query = new InternalItemsQuery
            {
                HasPath = true,
                HasAudioStream = false,
                MediaTypes = new string[] { MediaType.Video },
                Recursive = true,
                AncestorIds = items?.OfType<Series>().Select(s => s.InternalId)
                        .Union(items?.OfType<Episode>().Select(e => e.SeriesId)).ToArray()
            };

            var episodes = new BaseItem[0];
            if (query.AncestorIds.Length>0)
            {
                episodes = _libraryManager.GetItemList(query);
            }

            var favorites = FilterByFavorites(movies.Concat(episodes));

            bool includeExtra = Plugin.Instance.GetPluginOptions().IncludeExtra;
            _logger.Info("Include Extra: " + includeExtra);

            var results = FilterUnprocessed(favorites.OrderByDescending(i => i.PremiereDate)
                .Concat(includeExtra ? GetExtras(favorites) : Enumerable.Empty<BaseItem>()).ToList());
            return results;
        }

        public List<BaseItem> FetchItems()
        {
            InternalItemsQuery query = new InternalItemsQuery();
            query.HasPath = true;
            query.HasAudioStream = false;
            query.MediaTypes = new string[] { MediaType.Video, MediaType.Audio };
            query.OrderBy = new (string, SortOrder)[] { (ItemSortBy.PremiereDate, SortOrder.Descending) }; //PremiereDate refers to ReleaseDate
            BaseItem[] results = new BaseItem[0];
            
            bool includeExtra = Plugin.Instance.GetPluginOptions().IncludeExtra;
            _logger.Info("Include Extra: " + includeExtra);

            results = _libraryManager.GetItemList(query);
            if (includeExtra)
            {
                query.ExtraTypes = extraType;
                query.OrderBy = new (string, SortOrder)[] { (ItemSortBy.DateCreated, SortOrder.Descending) }; //PremiereDate is not available for extra
                BaseItem[] extras = _libraryManager.GetItemList(query);
                Array.Resize(ref results, results.Length + extras.Length);
                Array.Copy(extras, 0, results, results.Length - extras.Length, extras.Length);
            }
            return FilterUnprocessed(results.ToList());
        }

        private List<BaseItem> FilterUnprocessed(List<BaseItem> results)
        {
            _logger.Info("Number of items before: " + results.Count);
            bool strmOnly = Plugin.Instance.GetPluginOptions().StrmOnly;
            _logger.Info("Strm Only: " + strmOnly);

            List<BaseItem> items = new List<BaseItem>();
            foreach (BaseItem item in results)
            {
                if (!string.IsNullOrEmpty(item.Path) &&
                    strmOnly ? item.IsShortcut : true &&
                    item.GetMediaStreams().FindAll(i => i.Type == MediaStreamType.Video || i.Type == MediaStreamType.Audio).Count == 0)
                {
                    items.Add(item);
                }
                else
                {
                    _logger.Debug("Item dropped: " + item.Name + " - " + item.Path + " - " + item.GetType() + " - " + item.GetMediaStreams().Count);
                }
            }

            _logger.Info("Number of items dropped: " + (results.Count - items.Count));
            _logger.Info("Number of items after: " + items.Count);
            return items;
        }

        private IEnumerable<BaseItem> FilterByFavorites(IEnumerable<BaseItem> items)
        {
            var movies = allUsers
                .SelectMany(u => items?.OfType<Movie>()
                .Where(i => i.IsFavoriteOrLiked(u)));
            var episodes = allUsers
                .SelectMany(u => items?.OfType<Episode>()
                .GroupBy(e => e.SeriesId)
                .Where(g => g.Any(i => i.IsFavoriteOrLiked(u)) || g.First().Series.IsFavoriteOrLiked(u))
                .SelectMany(g => g)
                );
            return movies.Cast<BaseItem>().Concat(episodes.Cast<BaseItem>())
                .GroupBy(i => i.InternalId).Select(g => g.First());
        }

        private IEnumerable<BaseItem> GetExtras(IEnumerable<BaseItem> items)
        {
            var extras = items.SelectMany(i => i.GetExtras(extraType));
            return extras;
        }
    }
}