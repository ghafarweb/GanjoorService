﻿using GanjooRazor.Utils;
using GSpotifyProxy.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RMuseum.Models.Ganjoor;
using RMuseum.Models.Ganjoor.ViewModels;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;

namespace GanjooRazor.Pages
{
    [IgnoreAntiforgeryToken(Order = 1001)]
    public class SpotifyModel : PageModel
    {
        /// <summary>
        /// HttpClient instance
        /// </summary>
        private readonly HttpClient _httpClient;

        /// <summary>
        /// configuration
        /// </summary>
        private readonly IConfiguration Configuration;

        /// <summary>
        /// memory cache
        /// </summary>
        private readonly IMemoryCache _memoryCache;


        /// <summary>
        /// constructor
        /// </summary>
        /// <param name="httpClient"></param>
        /// <param name="configuration"></param>

        public SpotifyModel(HttpClient httpClient, IConfiguration configuration, IMemoryCache memoryCache)
        {
            _httpClient = httpClient;
            Configuration = configuration;
            _memoryCache = memoryCache;
        }

        /// <summary>
        /// is logged on
        /// </summary>
        public bool LoggedIn { get; set; }

        /// <summary>
        /// PoemId
        /// </summary>
        public int PoemId { get; set; }

        /// <summary>
        /// Last Error
        /// </summary>
        public string LastError { get; set; }

        /// <summary>
        /// Post Success
        /// </summary>
        public bool PostSuccess { get; set; }


        /// <summary>
        /// Inserted song Id
        /// </summary>
        public int InsertedSongId { get; set; }

        /// <summary>
        /// suggested (unapproved) songs
        /// </summary>
        public PoemMusicTrackViewModel[] SuggestedSongs { get; set; }

        /// <summary>
        /// api model
        /// </summary>
        [BindProperty]
        public PoemMusicTrackViewModel PoemMusicTrackViewModel { get; set; }

        private async Task _GetSuggestedSongs()
        {
            var response = await _httpClient.GetAsync($"{APIRoot.Url}/api/ganjoor/poem/{PoemId}/songs/?approved=false&trackType={(int)PoemMusicTrackType.Spotify}");

            if (response.StatusCode == HttpStatusCode.OK)
            {
                SuggestedSongs = JsonConvert.DeserializeObject<PoemMusicTrackViewModel[]>(await response.Content.ReadAsStringAsync());
            }
            else
            {
                SuggestedSongs = new PoemMusicTrackViewModel[] { };
            }
        }

        public async Task OnGetAsync()
        {
            PostSuccess = false;
            LastError = "";
            InsertedSongId = 0;
            LoggedIn = !string.IsNullOrEmpty(Request.Cookies["Token"]);

            if (!string.IsNullOrEmpty(Request.Query["p"]))
            {
                PoemId = int.Parse(Request.Query["p"]);
            }
            else
            {
                PoemMusicTrackViewModel.PoemId = 0;
            }

            await _GetSuggestedSongs();
        }

        public async Task<IActionResult> OnPostAsync()
        {
            PostSuccess = false;
            LastError = "";
            LoggedIn = !string.IsNullOrEmpty(Request.Cookies["Token"]);
            PoemId = PoemMusicTrackViewModel.PoemId = int.Parse(Request.Query["p"]);
            PoemMusicTrackViewModel.TrackType = PoemMusicTrackType.Spotify;
            InsertedSongId = 0;

            using (HttpClient secureClient = new HttpClient())
            {
                if (await GanjoorSessionChecker.PrepareClient(secureClient, Request, Response))
                {
                    var stringContent = new StringContent(JsonConvert.SerializeObject(PoemMusicTrackViewModel), Encoding.UTF8, "application/json");
                    var methodUrl = $"{APIRoot.Url}/api/ganjoor/song";
                    var response = await secureClient.PostAsync(methodUrl, stringContent);
                    if (!response.IsSuccessStatusCode)
                    {
                        LastError = JsonConvert.DeserializeObject<string>(await response.Content.ReadAsStringAsync());
                    }
                    else
                    {
                        InsertedSongId = JsonConvert.DeserializeObject<PoemMusicTrackViewModel>(await response.Content.ReadAsStringAsync()).Id;

                        PostSuccess = true;
                    }
                }
                else
                {
                    LastError = "لطفا از گنجور خارج و مجددا به آن وارد شوید.";
                }

                await _GetSuggestedSongs();
            }

            return Page();
        }


        /// <summary>
        /// search by artists name
        /// </summary>
        /// <param name="search"></param>
        /// <param name="secondtime"></param>
        /// <returns></returns>
        public async Task<IActionResult> OnPostSearchByArtistNameAsync(string search, bool secondtime = false)
        {
            List<NameIdUrlImage> artists = new List<NameIdUrlImage>();
            if (bool.Parse(Configuration["MockSpotify"]))
            {
                artists.Add(
                           new NameIdUrlImage()
                           {
                               Name = "محمدرضا شجریان",
                               Id = "1",
                               Url = "https://beeptunes.com/artist/3403349",
                               Image = "https://api.ganjoor.net/api/rimages/d32a98c3-a20c-4d37-45a9-08d93cffb0b7.jpg"
                           }
                           );
            }
            else
            {
                if (!_memoryCache.TryGetValue("SpotifyAccessToken", out string spotifyAccessToken))
                {
                    using (HttpClient secureClient = new HttpClient())
                    {
                        if (await GanjoorSessionChecker.PrepareClient(secureClient, Request, Response))
                        {
                            var responseOption = await secureClient.GetAsync($"{APIRoot.Url}/api/options/global/SpotifyAccessToken");
                            if (!responseOption.IsSuccessStatusCode)
                            {
                                spotifyAccessToken = "";
                                return new BadRequestObjectResult(JsonConvert.DeserializeObject<string>(await responseOption.Content.ReadAsStringAsync()));
                            }
                            else
                            {
                                string encryptedAccessToken = JsonConvert.DeserializeObject<string>(await responseOption.Content.ReadAsStringAsync());
                                spotifyAccessToken = EncDecUtil.Decrypt(encryptedAccessToken, Configuration.GetSection("Spotify")["Salt"]);
                                _memoryCache.Set("SpotifyAccessToken", spotifyAccessToken);
                            }
                        }
                        else
                        {
                            return new BadRequestObjectResult(JsonConvert.DeserializeObject<string>("لطفا از گنجور خارج و مجددا به آن وارد شوید."));
                        }
                    }
                }

                string spotifyToken = $"Bearer {spotifyAccessToken}";
                var request = new HttpRequestMessage(HttpMethod.Get,
                    $"https://api.spotify.com/v1/search?q={search}&type=artist");
                request.Headers.Add("Authorization", spotifyToken);
                var response = await _httpClient.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    var parsed = JObject.Parse(json);

                    foreach (JToken artist in parsed.SelectTokens("artists.items[*]"))
                    {
                        string imageUrl = "";
                        foreach (JToken image in artist.SelectTokens("images[*].url"))
                        {
                            imageUrl = image.Value<string>();
                            break;
                        }
                        artists.Add(
                            new NameIdUrlImage()
                            {
                                Name = artist.SelectToken("name").Value<string>(),
                                Id = artist.SelectToken("id").Value<string>(),
                                Url = artist.SelectToken("external_urls.spotify").Value<string>(),
                                Image = imageUrl
                            }
                            );
                    }

                }
                else
                {
                    if (!secondtime)
                    {
                        await _RefreshSpotifyToken();
                        return await OnPostSearchByArtistNameAsync(search, true);
                    }
                    return new BadRequestObjectResult(JsonConvert.DeserializeObject<string>(await response.Content.ReadAsStringAsync()));
                }
            }

            return new PartialViewResult()
            {
                ViewName = "_SpotifySearchPartial",
                ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
                {
                    Model = new _SpotifySearchPartialModel()
                    {
                        Artists = artists.ToArray()
                    }
                }
            };
        }

        private async Task _RefreshSpotifyToken()
        {
            if (_memoryCache.TryGetValue("SpotifyAccessToken", out string spotifyAccessToken))
            {
                using (HttpClient secureClient = new HttpClient())
                {
                    if (await GanjoorSessionChecker.PrepareClient(secureClient, Request, Response))
                    {
                        var responseOption = await secureClient.GetAsync($"{APIRoot.Url}/api/options/global/SpotifyAccessToken");
                        if (!responseOption.IsSuccessStatusCode)
                        {
                            return;
                        }
                        else
                        {
                            string encryptedAccessToken = JsonConvert.DeserializeObject<string>(await responseOption.Content.ReadAsStringAsync());
                            string newSpotifyAccessToken = EncDecUtil.Decrypt(encryptedAccessToken, Configuration.GetSection("Spotify")["Salt"]);
                            if(newSpotifyAccessToken != spotifyAccessToken)
                            {
                                _memoryCache.Set("SpotifyAccessToken", newSpotifyAccessToken);
                                return;
                            }
                            
                        }
                    }
                    else
                    {
                        return;
                    }
                }
            }

            string refresh_token;
            using (HttpClient secureClient = new HttpClient())
            {
                if (await GanjoorSessionChecker.PrepareClient(secureClient, Request, Response))
                {
                    var responseOption = await secureClient.GetAsync($"{APIRoot.Url}/api/options/global/SpotifyRefreshToken");
                    if (!responseOption.IsSuccessStatusCode)
                    {
                        return;
                    }
                    else
                    {
                        string encryptedRefreshToken = JsonConvert.DeserializeObject<string>(await responseOption.Content.ReadAsStringAsync());
                        refresh_token = EncDecUtil.Decrypt(encryptedRefreshToken, Configuration.GetSection("Spotify")["Salt"]);
                    }
                }
                else
                {
                    return;
                }
            }

            var nvc = new List<KeyValuePair<string, string>>();
            nvc.Add(new KeyValuePair<string, string>("grant_type", "refresh_token"));
            nvc.Add(new KeyValuePair<string, string>("refresh_token", refresh_token));
            var formContent = new FormUrlEncodedContent(nvc);
            var request = new HttpRequestMessage(HttpMethod.Post,
            "https://accounts.spotify.com/api/token");
            request.Content = formContent;
            string authValue = Convert.ToBase64String(new ASCIIEncoding().GetBytes($"{Configuration.GetSection("Spotify")["client_id"]}:{Configuration.GetSection("Spotify")["client_secret"]}"));
            request.Headers.Add("Authorization", $"Basic {authValue}");
            var response = await _httpClient.SendAsync(request);
            if (response.IsSuccessStatusCode)
            {
                string json = await response.Content.ReadAsStringAsync();
                var parsed = JObject.Parse(json);
                string access_token = parsed.SelectToken("access_token").Value<string>();
                string encryptedAccessToken = EncDecUtil.Encrypt(access_token, Configuration.GetSection("Spotify")["Salt"]);
                using (HttpClient secureClient = new HttpClient())
                {
                    if (await GanjoorSessionChecker.PrepareClient(secureClient, Request, Response))
                    {
                        var responseSaveOption = await secureClient.PutAsync($"{APIRoot.Url}/api/options/global/SpotifyAccessToken", new StringContent(JsonConvert.SerializeObject(encryptedAccessToken), Encoding.UTF8, "application/json"));
                        if (!responseSaveOption.IsSuccessStatusCode)
                        {
                            return;
                        }
                        _memoryCache.Set("SpotifyAccessToken", access_token);
                    }
                }

            }
        }

        /// <summary>
        /// fill artist albums
        /// </summary>
        /// <param name="artist">is an ID and consists of numeric and non-numeric characters</param>
        /// <returns></returns>
        public async Task<IActionResult> OnPostFillAlbumsAsync(string artist, bool secondtime = false)
        {
            List<NameIdUrlImage> albums = new List<NameIdUrlImage>();
            if (bool.Parse(Configuration["MockSpotify"]))
            {
                albums.Add(
                                 new NameIdUrlImage()
                                 {
                                     Name = "رباعیات خیام",
                                     Id = "1",
                                     Url = "https://beeptunes.com/track/3434445",
                                     Image = "https://api.ganjoor.net/api/rimages/d32a98c3-a20c-4d37-45a9-08d93cffb0b7.jpg"
                                 }
                                 );
            }
            else
            {
                if (!_memoryCache.TryGetValue("SpotifyAccessToken", out string spotifyAccessToken))
                {
                    using (HttpClient secureClient = new HttpClient())
                    {
                        if (await GanjoorSessionChecker.PrepareClient(secureClient, Request, Response))
                        {
                            var responseOption = await secureClient.GetAsync($"{APIRoot.Url}/api/options/global/SpotifyAccessToken");
                            if (!responseOption.IsSuccessStatusCode)
                            {
                                spotifyAccessToken = "";
                                return new BadRequestObjectResult(JsonConvert.DeserializeObject<string>(await responseOption.Content.ReadAsStringAsync()));
                            }
                            else
                            {
                                string encryptedAccessToken = JsonConvert.DeserializeObject<string>(await responseOption.Content.ReadAsStringAsync());
                                spotifyAccessToken = EncDecUtil.Decrypt(encryptedAccessToken, Configuration.GetSection("Spotify")["Salt"]);
                                _memoryCache.Set("SpotifyAccessToken", spotifyAccessToken);
                            }
                        }
                        else
                        {
                            return new BadRequestObjectResult(JsonConvert.DeserializeObject<string>("لطفا از گنجور خارج و مجددا به آن وارد شوید."));
                        }
                    }
                }

                string spotifyToken = $"Bearer {spotifyAccessToken}";
                int offest = 0;
                int limit = 50;
                bool newItems;
                do
                {
                    var request = new HttpRequestMessage(HttpMethod.Get,
                    $"https://api.spotify.com/v1/artists/{artist}/albums?limit={limit}&offset={offest}");
                    request.Headers.Add("Authorization", spotifyToken);

                    var response = await _httpClient.SendAsync(request);

                    if (response.IsSuccessStatusCode)
                    {
                        string json = await response.Content.ReadAsStringAsync();
                        var parsed = JObject.Parse(json);
                        newItems = false;
                        foreach (JToken album in parsed.SelectTokens("items[*]"))
                        {
                            newItems = true;
                            string imageUrl = "";
                            foreach (JToken image in album.SelectTokens("images[*].url"))
                            {
                                imageUrl = image.Value<string>();
                                break;
                            }
                            string album_type = album.SelectToken("album_type").Value<string>();
                            if (album_type == "album" || album_type == "single")
                            {
                                albums.Add(
                                new NameIdUrlImage()
                                {
                                    Name = album.SelectToken("name").Value<string>(),
                                    Id = album.SelectToken("id").Value<string>(),
                                    Url = album.SelectToken("external_urls.spotify").Value<string>(),
                                    Image = imageUrl
                                }
                                );
                            }
                        }

                    }
                    else
                    {
                        if (!secondtime && offest == 0)
                        {
                            await _RefreshSpotifyToken();
                            return await OnPostFillAlbumsAsync(artist, true);
                        }
                        return BadRequest(response.ToString());
                    }
                    offest += limit;
                }
                while (newItems);

                albums.Sort((a, b) => a.Name.CompareTo(b.Name));
            }


            return new OkObjectResult(albums.ToArray());
        }

        /// <summary>
        /// fill album tracks
        /// </summary>
        /// <param name="album">is an ID and consists of numeric and non-numeric characters</param>
        /// <returns></returns>
        public async Task<IActionResult> OnPostFillTracksAsync(string album, bool secondtime = false)
        {
            List<NameIdUrlImage> tracks = new List<NameIdUrlImage>();

            if (bool.Parse(Configuration["MockSpotify"]))
            {
                tracks.Add(
                              new NameIdUrlImage()
                              {
                                  Name = "من بی می ناب زیستن نتوانم",
                                  Id = "1",
                                  Url = "https://beeptunes.com/track/3434445",
                                  Image = "https://api.ganjoor.net/api/rimages/d32a98c3-a20c-4d37-45a9-08d93cffb0b7.jpg"
                              }
                              );
            }
            else
            {
                if (!_memoryCache.TryGetValue("SpotifyAccessToken", out string spotifyAccessToken))
                {
                    using (HttpClient secureClient = new HttpClient())
                    {
                        if (await GanjoorSessionChecker.PrepareClient(secureClient, Request, Response))
                        {
                            var responseOption = await secureClient.GetAsync($"{APIRoot.Url}/api/options/global/SpotifyAccessToken");
                            if (!responseOption.IsSuccessStatusCode)
                            {
                                spotifyAccessToken = "";
                                return new BadRequestObjectResult(JsonConvert.DeserializeObject<string>(await responseOption.Content.ReadAsStringAsync()));
                            }
                            else
                            {
                                string encryptedAccessToken = JsonConvert.DeserializeObject<string>(await responseOption.Content.ReadAsStringAsync());
                                spotifyAccessToken = EncDecUtil.Decrypt(encryptedAccessToken, Configuration.GetSection("Spotify")["Salt"]);
                                _memoryCache.Set("SpotifyAccessToken", spotifyAccessToken);
                            }
                        }
                        else
                        {
                            return new BadRequestObjectResult(JsonConvert.DeserializeObject<string>("لطفا از گنجور خارج و مجددا به آن وارد شوید."));
                        }
                    }
                }

                string spotifyToken = $"Bearer {spotifyAccessToken}";

                var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.spotify.com/v1/albums/{album}/tracks?limit=50");
                request.Headers.Add("Authorization", spotifyToken);

                var response = await _httpClient.SendAsync(request);

                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    var parsed = JObject.Parse(json);

                    foreach (JToken track in parsed.SelectTokens("items[*]"))
                    {
                        string imageUrl = "";
                        foreach (JToken image in track.SelectTokens("images[*].url"))
                        {
                            imageUrl = image.Value<string>();
                            break;
                        }
                        tracks.Add(
                             new NameIdUrlImage()
                             {
                                 Name = track.SelectToken("name").Value<string>(),
                                 Id = track.SelectToken("id").Value<string>(),
                                 Url = track.SelectToken("external_urls.spotify").Value<string>(),
                                 Image = imageUrl
                             }
                             );
                    }
                }
                else
                {
                    if (!secondtime)
                    {
                        await _RefreshSpotifyToken();
                        return await OnPostFillTracksAsync(album, true);
                    }
                    return BadRequest(response.ToString());
                }
            }



            return new OkObjectResult(tracks.ToArray());
        }

        /// <summary>
        /// search by track title
        /// </summary>
        /// <param name="search"></param>
        /// <returns></returns>
        public async Task<IActionResult> OnPostSearchByTrackTitleAsync(string search, bool secondtime = false)
        {
            List<TrackQueryResult> tracks = new List<TrackQueryResult>();

            if (bool.Parse(Configuration["MockSpotify"]))
            {
                tracks.Add(
                         new TrackQueryResult()
                         {
                             Name = "من بی می ناب زیستن نتوانم",
                             Id = "1",
                             Url = "https://beeptunes.com/track/3434445",
                             Image = "https://api.ganjoor.net/api/rimages/d32a98c3-a20c-4d37-45a9-08d93cffb0b7.jpg",
                             ArtistName = "محمدرضا شجریان",
                             ArtistId = "1",
                             ArtistUrl = "https://beeptunes.com/artist/3403349",
                             AlbumName = "رباعیات خیام",
                             AlbumId = "1",
                             AlbunUrl = "https://beeptunes.com/album/3412806"
                         }
                         );
            }
            else
            {
                if (!_memoryCache.TryGetValue("SpotifyAccessToken", out string spotifyAccessToken))
                {
                    using (HttpClient secureClient = new HttpClient())
                    {
                        if (await GanjoorSessionChecker.PrepareClient(secureClient, Request, Response))
                        {
                            var responseOption = await secureClient.GetAsync($"{APIRoot.Url}/api/options/global/SpotifyAccessToken");
                            if (!responseOption.IsSuccessStatusCode)
                            {
                                spotifyAccessToken = "";
                                return new BadRequestObjectResult(JsonConvert.DeserializeObject<string>(await responseOption.Content.ReadAsStringAsync()));
                            }
                            else
                            {
                                string encryptedAccessToken = JsonConvert.DeserializeObject<string>(await responseOption.Content.ReadAsStringAsync());
                                spotifyAccessToken = EncDecUtil.Decrypt(encryptedAccessToken, Configuration.GetSection("Spotify")["Salt"]);
                                _memoryCache.Set("SpotifyAccessToken", spotifyAccessToken);
                            }
                        }
                        else
                        {
                            return new BadRequestObjectResult(JsonConvert.DeserializeObject<string>("لطفا از گنجور خارج و مجددا به آن وارد شوید."));
                        }
                    }
                }

                string spotifyToken = $"Bearer {spotifyAccessToken}";

                var request = new HttpRequestMessage(HttpMethod.Get,
                $"https://api.spotify.com/v1/search?q={search}&type=track");
                request.Headers.Add("Authorization", spotifyToken);

                var response = await _httpClient.SendAsync(request);


                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    var parsed = JObject.Parse(json);

                    foreach (JToken track in parsed.SelectTokens("tracks.items[*]"))
                    {
                        string imageUrl = "";


                        NameIdUrlImage artistInfo = new NameIdUrlImage()
                        {
                            Id = "0",
                            Name = "",
                            Url = "",
                            Image = ""
                        };
                        foreach (JToken artist in track.SelectTokens("artists[*]"))
                        {
                            artistInfo.Name = artist.SelectToken("name").Value<string>();
                            artistInfo.Id = artist.SelectToken("id").Value<string>();
                            artistInfo.Url = artist.SelectToken("external_urls.spotify").Value<string>();
                            break;
                        }
                        NameIdUrlImage albumInfo = new NameIdUrlImage()
                        {
                            Id = "0",
                            Name = "",
                            Url = "",
                            Image = ""
                        };
                        JToken album = track.SelectToken("album");
                        if (album != null)
                        {
                            albumInfo.Name = album.SelectToken("name").Value<string>();
                            albumInfo.Id = album.SelectToken("id").Value<string>();
                            albumInfo.Url = album.SelectToken("external_urls.spotify").Value<string>();

                            foreach (JToken image in album.SelectTokens("images[*].url"))
                            {
                                imageUrl = image.Value<string>();
                                break;
                            }
                        }
                        tracks.Add(
                            new TrackQueryResult()
                            {
                                Name = track.SelectToken("name").Value<string>(),
                                Id = track.SelectToken("id").Value<string>(),
                                Url = track.SelectToken("external_urls.spotify").Value<string>(),
                                Image = imageUrl,
                                ArtistName = artistInfo.Name,
                                ArtistId = artistInfo.Id,
                                ArtistUrl = artistInfo.Url,
                                AlbumName = albumInfo.Name,
                                AlbumId = albumInfo.Id,
                                AlbunUrl = albumInfo.Url
                            }
                            );
                    }

                }
                else
                {
                    if (!secondtime)
                    {
                        await _RefreshSpotifyToken();
                        return await OnPostSearchByTrackTitleAsync(search, true);
                    }
                    return BadRequest(response.ToString());
                }
            }

            return new PartialViewResult()
            {
                ViewName = "_SpotifySearchPartial",
                ViewData = new ViewDataDictionary(new EmptyModelMetadataProvider(), new ModelStateDictionary())
                {
                    Model = new _SpotifySearchPartialModel()
                    {
                        Tracks = tracks.ToArray()
                    }
                }
            };
        }
    }
}
