﻿using Dapper;
using ganjoor;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MySql.Data.MySqlClient;
using Renci.SshNet;
using RMuseum.DbContext;
using RMuseum.Models.Ganjoor;
using RMuseum.Models.GanjoorAudio;
using RMuseum.Models.GanjoorAudio.ViewModels;
using RMuseum.Models.UploadSession;
using RMuseum.Models.UploadSession.ViewModels;
using RMuseum.Services.Implementation.ImportedFromDesktopGanjoor;
using RSecurityBackend.Models.Generic;
using RSecurityBackend.Services.Implementation;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace RMuseum.Services.Implementation
{

    /// <summary>
    /// Audio Narration Service Implementation
    /// </summary>
    public class RecitationService : IRecitationService
    {

        


        /// <summary>
        /// returns list of narrations
        /// </summary>
        /// <param name="paging"></param>
        /// <param name="filteredUserId">send Guid.Empty if you want all narrations</param>
        /// <param name="status"></param>
        /// <returns></returns>
        public async Task<RServiceResult<(PaginationMetadata PagingMeta, RecitationViewModel[] Items)>> GetAll(PagingParameterModel paging, Guid filteredUserId, AudioReviewStatus status)
        {
            try
            {
                //whenever I had not a reference to audio.Owner in the final selection it became null, so this strange arrangement is not all because of my stupidity!
                var source =
                     from audio in _context.Recitations
                     .Include(a => a.Owner)
                     .Where(a =>
                            (filteredUserId == Guid.Empty || a.OwnerId == filteredUserId)
                            &&
                            (status == AudioReviewStatus.All || a.ReviewStatus == status)
                     )
                    .OrderByDescending(a => a.UploadDate)
                     join poem in _context.GanjoorPoems
                     on audio.GanjoorPostId equals poem.Id
                     select new RecitationViewModel(audio, audio.Owner, poem);

                (PaginationMetadata PagingMeta, RecitationViewModel[] Items) paginatedResult =
                    await QueryablePaginator<RecitationViewModel>.Paginate(source, paging);

                return new RServiceResult<(PaginationMetadata PagingMeta, RecitationViewModel[] Items)>(paginatedResult);
            }
            catch (Exception exp)
            {
                return new RServiceResult<(PaginationMetadata PagingMeta, RecitationViewModel[] Items)>((PagingMeta: null, Items: null), exp.ToString());
            }
        }

        /// <summary>
        /// return selected narration information
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public async Task<RServiceResult<RecitationViewModel>> Get(int id)
        {
            try
            {
                //whenever I had not a reference to audio.Owner in the final selection it became null, so this strange arrangement is not all because of my stupidity!
                var source =
                     from audio in _context.Recitations
                     .Include(a => a.Owner)
                     .Where(a => a.Id == id)
                     join poem in _context.GanjoorPoems
                     on audio.GanjoorPostId equals poem.Id
                     select new RecitationViewModel(audio, audio.Owner, poem);

                var narration = await source.SingleOrDefaultAsync();
                return new RServiceResult<RecitationViewModel>(narration);
            }
            catch (Exception exp)
            {
                return new RServiceResult<RecitationViewModel>(null, exp.ToString());
            }
        }

        /// <summary>
        /// Gets Verse Sync Range Information
        /// </summary>
        /// <param name="id">narration id</param>
        /// <returns></returns>
        public async Task<RServiceResult<RecitationVerseSync[]>> GetPoemNarrationVerseSyncArray(int id)
        {
            try
            {
                var narration = await _context.Recitations.Where(a => a.Id == id).SingleOrDefaultAsync();
                var verses = await _context.GanjoorVerses.Where(v => v.PoemId == narration.GanjoorPostId).OrderBy(v => v.VOrder).ToListAsync();

                string xml = File.ReadAllText(narration.LocalXmlFilePath);

                List<RecitationVerseSync> verseSyncs = new List<RecitationVerseSync>();

                XElement elObject = XDocument.Parse(xml).Root;
                foreach (var syncInfo in elObject.Element("PoemAudio").Element("SyncArray").Elements("SyncInfo"))
                {
                    int verseOrder = int.Parse(syncInfo.Element("VerseOrder").Value);
                    if (verseOrder < 0) //this happens, seems to be a bug, I did not trace it yet
                        verseOrder = 0;
                    verseOrder++;
                    var verse = verses.Where(v => v.VOrder == verseOrder).SingleOrDefault();
                    if(verse != null)
                    {
                        verseSyncs.Add(new RecitationVerseSync()
                        {
                            VerseOrder = verseOrder,
                            VerseText = verse.Text,
                            AudioStartMilliseconds = int.Parse(syncInfo.Element("AudioMiliseconds").Value)
                        });
                    }
                }

                return new RServiceResult<RecitationVerseSync[]>(verseSyncs.ToArray());
            }
            catch (Exception exp)
            {
                return new RServiceResult<RecitationVerseSync[]>(null, exp.ToString());
            }
        }

        /// <summary>
        /// validate PoemNarrationViewModel
        /// </summary>
        /// <param name="p"></param>
        /// <returns></returns>
        private static string GetPoemNarrationValidationError(RecitationViewModel p)
        {
            if (p.AudioArtist.Length < 3)
            {
                return "نام خوانشگر باید حداقل شامل سه نویسه باشد.";
            }

            string s = LanguageUtils.GetFirstNotMatchingCharacter(p.AudioArtist, LanguageUtils.PersianAlphabet, " ‌");
            if (s != "")
            {
                return $"نام فقط باید شامل حروف فارسی و فاصله باشد. اولین حرف غیرمجاز = {s}";
            }

            if (!string.IsNullOrEmpty(p.AudioArtistUrl))
            {
                bool result = Uri.TryCreate(p.AudioArtistUrl, UriKind.Absolute, out Uri uriResult)
                    && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);

                if (!result)
                {
                    return $"نشانی وب خوانشگر نامعتبر است.";
                }
            }

            if(!string.IsNullOrEmpty(p.AudioSrcUrl))
            {
                bool result = Uri.TryCreate(p.AudioSrcUrl, UriKind.Absolute, out Uri uriResult)
               && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);

                if (!result)
                {
                    return $"نشانی وب منبع نامعتبر است.";
                }
            }
            

            return "";
        }

        /// <summary>
        /// updates metadata for narration
        /// </summary>
        /// <param name="id"></param>
        /// <param name="metadata"></param>
        /// <returns></returns>
        public async Task<RServiceResult<RecitationViewModel>> UpdatePoemNarration(int id, RecitationViewModel metadata)
        {
            try
            {

                metadata.AudioTitle = metadata.AudioTitle.Trim();
                metadata.AudioArtist = metadata.AudioArtist.Trim();
                metadata.AudioArtistUrl = metadata.AudioArtistUrl.Trim();
                metadata.AudioSrc = metadata.AudioSrc.Trim();
                metadata.AudioSrcUrl = metadata.AudioSrcUrl.Trim();

                string err = GetPoemNarrationValidationError(metadata);
                if(!string.IsNullOrEmpty(err))
                {
                    return new RServiceResult<RecitationViewModel>(null, err);
                }

                Recitation narration =  await _context.Recitations.Include(a => a.Owner).Where(a => a.Id == id).SingleOrDefaultAsync();
                if(narration == null)
                    return new RServiceResult<RecitationViewModel>(null, "404");
                narration.AudioTitle = metadata.AudioTitle;
                narration.AudioArtist = metadata.AudioArtist;
                narration.AudioArtistUrl = metadata.AudioArtistUrl;
                narration.AudioSrc = metadata.AudioSrc;
                narration.AudioSrcUrl = metadata.AudioSrcUrl;
                narration.ReviewStatus = metadata.ReviewStatus;
                _context.Recitations.Update(narration);
                await _context.SaveChangesAsync();
                return new RServiceResult<RecitationViewModel>(new RecitationViewModel(narration, narration.Owner, await _context.GanjoorPoems.Where(p => p.Id == narration.GanjoorPostId).SingleOrDefaultAsync()));
            }
            catch (Exception exp)
            {
                return new RServiceResult<RecitationViewModel>(null, exp.ToString());
            }
        }

        
        /// <summary>
        /// imports data from ganjoor MySql database
        /// </summary>
        /// <param name="ownerRAppUserId">User Id which becomes owner of imported data</param>
        public async Task<RServiceResult<bool>> OneTimeImport(Guid ownerRAppUserId)
        {
            try
            {
                Recitation sampleCheck = await _context.Recitations.FirstOrDefaultAsync();
                if(sampleCheck != null)
                {
                    return new RServiceResult<bool>(false, "OneTimeImport is a one time operation and cannot be called multiple times.");
                }
                using (MySqlConnection connection = new MySqlConnection
                    (
                    $"server={Configuration.GetSection("AudioMySqlServer")["Server"]};uid={Configuration.GetSection("AudioMySqlServer")["Username"]};pwd={Configuration.GetSection("AudioMySqlServer")["Password"]};database={Configuration.GetSection("AudioMySqlServer")["Database"]};charset=utf8"
                    ))
                {
                    connection.Open();
                    //I thought that result Id fields would become corresponant to order of selection (and later insertions) but it is not
                    //the case in batch insertion, so this ORDER BY clause is useless unless we do save every time we insert a record
                    //which I guess might take much longer
                    using(MySqlDataAdapter src = new MySqlDataAdapter(
                        "SELECT audio_ID, audio_post_ID, audio_order, audio_xml, audio_ogg, audio_mp3, " +
                        "audio_title,  audio_artist, audio_artist_url, audio_src,  audio_src_url, audio_guid, " +
                        "audio_fchecksum,  audio_mp3bsize,  audio_oggbsize,  audio_date " +
                        "FROM ganja_gaudio ORDER BY audio_date",
                        connection
                        ))
                    {
                        using(DataTable srcData = new DataTable())
                        {
                            await src.FillAsync(srcData);

                            int audioSyncStatus = (int)AudioSyncStatus.SynchronizedOrRejected;

                            

                            foreach (DataRow row in srcData.Rows)
                            {
                                Recitation newRecord = new Recitation()
                                {
                                    OwnerId = ownerRAppUserId,
                                    GanjoorAudioId = int.Parse(row["audio_ID"].ToString()),
                                    GanjoorPostId = (int)row["audio_post_ID"],
                                    AudioOrder = (int)row["audio_order"],
                                    AudioTitle = row["audio_title"].ToString(),
                                    AudioArtist = row["audio_artist"].ToString(),
                                    AudioArtistUrl = row["audio_artist_url"].ToString(),
                                    AudioSrc = row["audio_src"].ToString(),
                                    AudioSrcUrl = row["audio_src_url"].ToString(),
                                    LegacyAudioGuid = new Guid(row["audio_guid"].ToString()),
                                    Mp3FileCheckSum = row["audio_fchecksum"].ToString(),
                                    Mp3SizeInBytes = (int)row["audio_mp3bsize"],
                                    OggSizeInBytes = (int)row["audio_oggbsize"],
                                    UploadDate = (DateTime)row["audio_date"],
                                    AudioSyncStatus = audioSyncStatus,
                                    ReviewStatus = AudioReviewStatus.Approved
                                };
                                newRecord.FileLastUpdated = newRecord.UploadDate;
                                newRecord.ReviewDate = newRecord.UploadDate;
                                string audio_xml = row["audio_xml"].ToString();
                                //sample audio_xml value: /i/a/x/11876-Simorgh.xml
                                audio_xml = audio_xml.Substring("/i/".Length); // /i/a/x/11876-Simorgh.xml -> a/x/11876-Simorgh.xml
                                newRecord.SoundFilesFolder = audio_xml.Substring(0, audio_xml.IndexOf('/')); //(a)
                                string targetForAudioFile = Path.Combine(Configuration.GetSection("AudioUploadService")["LocalAudioRepositoryPath"], newRecord.SoundFilesFolder);
                                string targetForXmlAudioFile = Path.Combine(targetForAudioFile, "x");
                               
                                newRecord.FileNameWithoutExtension = Path.GetFileNameWithoutExtension(audio_xml.Substring(audio_xml.LastIndexOf('/') + 1)); //(11876-Simorgh)
                                newRecord.LocalMp3FilePath = Path.Combine(targetForAudioFile, $"{newRecord.FileNameWithoutExtension}.mp3");
                                newRecord.LocalXmlFilePath = Path.Combine(targetForXmlAudioFile, $"{newRecord.FileNameWithoutExtension}.xml");

                                _context.Recitations.Add(newRecord);
                                await _context.SaveChangesAsync(); //this logically should be outside this loop, 
                                                                   //but it messes with the order of records so I decided 
                                                                   //to wait a little longer and have an ordered set of records
                            }
                        }
                       
                    }
                }
                string err = await BuildProfilesFromExistingData(ownerRAppUserId);
                if(!string.IsNullOrEmpty(err))
                    return new RServiceResult<bool>(false, err);
                return new RServiceResult<bool>(true);
            }
            catch(Exception exp)
            {
                return new RServiceResult<bool>(false, exp.ToString());
            }
        }

        /// <summary>
        /// build profiles from exisng narrations data
        /// </summary>
        /// <param name="ownerRAppUserId">User Id which becomes owner of imported data</param>
        /// <returns>error string if occurs</returns>
        public async Task<string> BuildProfilesFromExistingData(Guid ownerRAppUserId)
        {
            try
            {
                List<UserRecitationProfile> profiles =
                     await _context.Recitations
                     .GroupBy(audio => new { audio.AudioArtist, audio.AudioArtistUrl, audio.AudioSrc, audio.AudioSrcUrl })
                     .OrderByDescending(g => g.Count())
                     .Select(g => new UserRecitationProfile()
                     {
                         UserId = ownerRAppUserId,
                         ArtistName = g.Key.AudioArtist,
                         ArtistUrl = g.Key.AudioArtistUrl,
                         AudioSrc = g.Key.AudioSrc,
                         AudioSrcUrl = g.Key.AudioSrcUrl,
                         IsDefault = false
                     }
                     ).ToListAsync();
                foreach(UserRecitationProfile profile in profiles)
                {
                    Recitation narration = 
                        await _context.Recitations.Where(audio =>
                                                audio.AudioArtist == profile.ArtistName
                                                &&
                                                audio.AudioArtistUrl == profile.ArtistUrl
                                                &&
                                                audio.AudioSrc == profile.AudioSrc
                                                &&
                                                audio.AudioSrcUrl == profile.AudioSrcUrl
                                                //&&
                                                //audio.FileNameWithoutExtension.Contains('-')
                                                ).FirstOrDefaultAsync();
                    string ext = "";
                    if (narration != null && narration.FileNameWithoutExtension.IndexOf('-') != -1)
                    {
                        ext = narration.FileNameWithoutExtension.Substring(narration.FileNameWithoutExtension.IndexOf('-') + 1);
                    }
                    if(ext.Length < 2)
                    {
                        string[] parts = profile.ArtistName.Split(" ", StringSplitOptions.RemoveEmptyEntries);
                        if(parts.Length < 2)
                        {
                            ext = GPersianTextSync.Farglisize(profile.ArtistName).ToLower();
                            if (ext.Length > 3)
                                ext = ext.Substring(0, 3);
                        }
                        else
                        {
                            ext = "";
                            foreach (string part in parts)
                            {
                                string farglisi = GPersianTextSync.Farglisize(part).ToLower();
                                if (!string.IsNullOrEmpty(farglisi))
                                    ext += farglisi[0];
                            }
                        }
                    }
                    profile.FileSuffixWithoutDash = ext;
                    profile.Name = profile.ArtistName;
                    int pIndex = 1;
                    while((await _context.UserRecitationProfiles.Where(p => p.UserId == ownerRAppUserId && p.Name == profile.Name).SingleOrDefaultAsync())!=null)
                    {
                        pIndex++;
                        profile.Name = $"{profile.ArtistName} {GPersianTextSync.Sync(pIndex.ToString())}";
                    }
                    _context.UserRecitationProfiles.Add(profile);
                    await _context.SaveChangesAsync(); //this logically should be outside this loop, 
                                                       //but it messes with the order of records so I decided 
                                                       //to wait a little longer and have an ordered set of records
                }
                return "";
            }
            catch (Exception exp)
            {
                return exp.ToString();
            }
        }

        /// <summary>
        /// Initiate New Upload Session for audio
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="replace"></param>
        /// <returns></returns>
        public async Task<RServiceResult<UploadSession>> InitiateNewUploadSession(Guid userId, bool replace)
        {
            try
            {
                UserRecitationProfile defProfile = await _context.UserRecitationProfiles.Where(p => p.UserId == userId && p.IsDefault == true).FirstOrDefaultAsync();
                if (defProfile == null)
                {
                    return new RServiceResult<UploadSession>(null, "نمایهٔ پیش‌فرض شما مشخص نیست. لطفا پیش از ارسال خوانش نمایهٔ پیش‌فرض خود را تعریف کنید.");
                }

                UploadSession session = new UploadSession()
                {
                    SessionType = UploadSessionType.NewAudio,
                    UseId = userId,
                    UploadStartTime = DateTime.Now,
                    Status = UploadSessionProcessStatus.NotStarted,
                    ProcessProgress = 0,
                    User = await _context.Users.Where(u => u.Id == userId).FirstOrDefaultAsync() //this would be referenced later and is needed
                };
                await _context.UploadSessions.AddAsync(session);
                await _context.SaveChangesAsync();

                return new RServiceResult<UploadSession>(session);
                  
            }
            catch(Exception exp)
            {
                return new RServiceResult<UploadSession>(null, exp.ToString());
            }
        }

        /// <summary>
        /// Save uploaded file
        /// </summary>
        /// <param name="uploadedFile"></param>
        /// <returns></returns>
        public async Task<RServiceResult<UploadSessionFile>> SaveUploadedFile(IFormFile uploadedFile)
        {
            try
            {
                UploadSessionFile file = new UploadSessionFile()
                {
                    ContentDisposition = uploadedFile.ContentDisposition,
                    ContentType = uploadedFile.ContentType,
                    FileName = uploadedFile.FileName,
                    Length = uploadedFile.Length,
                    Name = uploadedFile.Name,
                    ProcessResult = false,
                    ProcessResultMsg = "پردازش نشده (فایلهای mp3‌ که مشخصات آنها در فایلهای xml ارسالی یافت نشود پردازش نمی‌شوند)."
                };

                string ext = Path.GetExtension(file.FileName).ToLower();
                if(ext != ".mp3" && ext != ".xml" && ext != ".ogg")
                {
                    file.ProcessResultMsg = "تنها فایلهای با پسوند mp3، xml و ogg قابل قبول هستند.";
                }
                else
                {
                    if (!Directory.Exists(Configuration.GetSection("AudioUploadService")["TempUploadPath"]))
                    {
                        try
                        {
                            Directory.CreateDirectory(Configuration.GetSection("AudioUploadService")["TempUploadPath"]);
                        }
                        catch
                        {
                            return new RServiceResult<UploadSessionFile>(null, $"ProcessImage: create dir failed {Configuration.GetSection("AudioUploadService")["TempUploadPath"]}");
                        }
                    }

                    string filePath = Path.Combine(Configuration.GetSection("AudioUploadService")["TempUploadPath"], file.FileName);
                    while(File.Exists(filePath))
                    {
                        filePath = Path.Combine(Configuration.GetSection("AudioUploadService")["TempUploadPath"], Guid.NewGuid().ToString() + ext);
                    }
                    using (FileStream fsMain = new FileStream(filePath, FileMode.Create))
                    {
                         await uploadedFile.CopyToAsync(fsMain);
                    }
                    file.FilePath = filePath;
                }                

                return new RServiceResult<UploadSessionFile>(file);

            }
            catch (Exception exp)
            {
                return new RServiceResult<UploadSessionFile>(null, exp.ToString());
            }
        }

        /// <summary>
        /// finalize upload session (add files)
        /// </summary>
        /// <param name="session"></param>
        /// <param name="files"></param>
        /// <returns></returns>
        public async Task<RServiceResult<UploadSession>> FinalizeNewUploadSession(UploadSession session, UploadSessionFile[] files)
        {
            try
            {
                session.UploadedFiles = files;
                session.UploadEndTime = DateTime.Now;

                _context.UploadSessions.Update(session);
                await _context.SaveChangesAsync();

                _backgroundTaskQueue.QueueBackgroundWorkItem
                    (
                    async token =>
                    {
                        using (RMuseumDbContext context = new RMuseumDbContext(Configuration)) //this is long running job, so _context might be already been freed/collected by GC
                        {
                            session.ProcessStartTime = DateTime.Now;
                            double fileCount = session.UploadedFiles.Count;
                            int processFilesCount = 0;
                            List<UploadSessionFile> mp3files = new List<UploadSessionFile>();
                            foreach (UploadSessionFile file in session.UploadedFiles.Where(file => Path.GetExtension(file.FilePath) == ".mp3").ToList())
                            {
                                processFilesCount++;
                                session.ProcessProgress = (int)(processFilesCount / fileCount * 100.0);
                                try
                                {
                                    file.MP3FileCheckSum = PoemAudio.ComputeCheckSum(file.FilePath);
                                    mp3files.Add(file); 
                                }
                                catch (Exception exp)
                                {
                                    session.UploadedFiles.Where(f => f.Id == file.Id).SingleOrDefault().ProcessResultMsg = exp.ToString();
                                    context.UploadSessions.Update(session);
                                    await context.SaveChangesAsync();
                                }
                            }

                            UserRecitationProfile defProfile = await context.UserRecitationProfiles.Where(p => p.UserId == session.UseId && p.IsDefault == true).FirstOrDefaultAsync(); //this should not be null

                            foreach (UploadSessionFile file in session.UploadedFiles.Where(file => Path.GetExtension(file.FilePath) == ".xml").ToList())
                            {
                                try
                                {
                                    //although each xml can theorically contain more than one file information
                                    //this assumption was never implemented and used in Desktop Ganjoor which produces this xml file
                                    //within the loop code the file is moved somewhere else and if the loop reaches is unexpected second path
                                    //the code would fail!
                                    foreach (PoemAudio audio in PoemAudioListProcessor.Load(file.FilePath)) 
                                    {
                                        if( await context.Recitations.Where(a => a.Mp3FileCheckSum == audio.FileCheckSum).SingleOrDefaultAsync() != null)
                                        {
                                            session.UploadedFiles.Where(f => f.Id == file.Id).SingleOrDefault().ProcessResultMsg = "فایل صوتیی همسان با فایل ارسالی پیشتر آپلود شده است.";
                                            context.UploadSessions.Update(session);
                                        }
                                        else
                                        {
                                            string soundFilesFolder = Configuration.GetSection("AudioUploadService")["TempUploadPath"];
                                            string currentTargetFolder = Configuration.GetSection("AudioUploadService")["CurrentSoundFilesFolder"];
                                            string targetPathForAudioFiles = Path.Combine(Configuration.GetSection("AudioUploadService")["LocalAudioRepositoryPath"], currentTargetFolder);
                                            if (!Directory.Exists(targetPathForAudioFiles))
                                            {
                                                Directory.CreateDirectory(targetPathForAudioFiles);
                                            }
                                            string targetPathForXmlFiles = Path.Combine(targetPathForAudioFiles, "x");
                                            if (!Directory.Exists(targetPathForXmlFiles))
                                            {
                                                Directory.CreateDirectory(targetPathForXmlFiles);
                                            }

                                            string fileNameWithoutExtension = $"{audio.PoemId}-{defProfile.FileSuffixWithoutDash}";
                                            int tmp = 1;
                                            while
                                            (
                                            File.Exists(Path.Combine(targetPathForAudioFiles, $"{fileNameWithoutExtension}.mp3"))
                                            ||
                                            File.Exists(Path.Combine(targetPathForXmlFiles, $"{fileNameWithoutExtension}.xml"))
                                            )
                                            {
                                                fileNameWithoutExtension = $"{audio.PoemId}-{defProfile.FileSuffixWithoutDash}{tmp}";
                                                tmp++;
                                            }


                                            string localXmlFilePath = Path.Combine(targetPathForXmlFiles, $"{fileNameWithoutExtension}.xml");
                                            File.Move(file.FilePath, localXmlFilePath); //this is the movemnet I talked about earlier


                                            string localMp3FilePath = Path.Combine(targetPathForAudioFiles, $"{fileNameWithoutExtension}.mp3");

                                            UploadSessionFile mp3file = mp3files.Where(mp3 => mp3.MP3FileCheckSum == audio.FileCheckSum).SingleOrDefault();
                                            if (mp3file == null)
                                            {
                                                session.UploadedFiles.Where(f => f.Id == file.Id).SingleOrDefault().ProcessResultMsg = "فایل mp3 متناظر یافت نشد (توجه فرمایید که همنامی اهمیت ندارد و فایل mp3 ارسالی باید دقیقاً همان فایلی باشد که همگامی با آن صورت گرفته است. اگر بعداً آن را جایگزین کرده‌اید مشخصات آن با مشخصات درج شده در فایل xml همسان نخواهد بود).";
                                                context.UploadSessions.Update(session);
                                            }
                                            else
                                            {
                                                File.Move(mp3file.FilePath, localMp3FilePath);
                                                int mp3fileSize = File.ReadAllBytes(localMp3FilePath).Length;

                                                bool replace = false;
                                                if(session.SessionType == UploadSessionType.ReplaceAudio)
                                                {
                                                    Recitation existing =  await context.Recitations.Where(r => r.OwnerId == session.UseId && r.GanjoorPostId == audio.PoemId && r.AudioArtist == defProfile.ArtistName).FirstOrDefaultAsync();
                                                    if(existing != null)
                                                    {
                                                        replace = true;

                                                        File.Move(localXmlFilePath, existing.LocalXmlFilePath, true);
                                                        File.Move(localMp3FilePath, existing.LocalMp3FilePath, true);
                                                        existing.Mp3FileCheckSum = audio.FileCheckSum;
                                                        existing.Mp3SizeInBytes = mp3fileSize;
                                                        existing.FileLastUpdated = session.UploadEndTime;
                                                        existing.AudioSyncStatus = (int)AudioSyncStatus.SoundFilesChanged;

                                                        context.Recitations.Update(existing);

                                                        await context.SaveChangesAsync();

                                                        _backgroundTaskQueue.QueueBackgroundWorkItem
                                                            (
                                                            async token =>
                                                            {
                                                            using (RMuseumDbContext publishcontext = new RMuseumDbContext(Configuration)) //this is long running job, so _context might be already been freed/collected by GC
                                                                                    {
                                                               await _PublishNarration(existing, publishcontext, true);
                                                            }
                                                            });

                                                    }
                                                }


                                                if(!replace)
                                                {
                                                    Guid legacyAudioGuid = audio.SyncGuid;
                                                    while (
                                                        (await context.Recitations.Where(a => a.LegacyAudioGuid == legacyAudioGuid).FirstOrDefaultAsync()) != null
                                                        )
                                                    {
                                                        legacyAudioGuid = Guid.NewGuid();
                                                    }


                                                    Recitation narration = new Recitation()
                                                    {
                                                        GanjoorPostId = audio.PoemId,
                                                        OwnerId = session.UseId,
                                                        GanjoorAudioId = 1 + await context.Recitations.OrderByDescending(a => a.GanjoorAudioId).Select(a => a.GanjoorAudioId).FirstOrDefaultAsync(),
                                                        AudioOrder = 1 + await context.Recitations.Where(a => a.GanjoorPostId == audio.PoemId).OrderByDescending(a => a.GanjoorAudioId).Select(a => a.GanjoorAudioId).FirstOrDefaultAsync(),
                                                        FileNameWithoutExtension = fileNameWithoutExtension,
                                                        SoundFilesFolder = currentTargetFolder,
                                                        AudioTitle = string.IsNullOrEmpty(audio.PoemTitle) ? audio.Description : audio.PoemTitle,
                                                        AudioArtist = defProfile.ArtistName,
                                                        AudioArtistUrl = defProfile.ArtistUrl,
                                                        AudioSrc = defProfile.AudioSrc,
                                                        AudioSrcUrl = defProfile.AudioSrcUrl,
                                                        LegacyAudioGuid = legacyAudioGuid,
                                                        Mp3FileCheckSum = audio.FileCheckSum,
                                                        Mp3SizeInBytes = mp3fileSize,
                                                        OggSizeInBytes = 0,
                                                        UploadDate = session.UploadEndTime,
                                                        FileLastUpdated = session.UploadEndTime,
                                                        LocalMp3FilePath = localMp3FilePath,
                                                        LocalXmlFilePath = localXmlFilePath,
                                                        AudioSyncStatus = (int)AudioSyncStatus.NewItem,
                                                        ReviewStatus = AudioReviewStatus.Draft
                                                    };

                                                    if (narration.AudioTitle.IndexOf("فایل صوتی") == 0) //no modification on title
                                                    {
                                                        GanjoorPoem poem = await context.GanjoorPoems.Where(p => p.Id == audio.PoemId).SingleOrDefaultAsync();
                                                        if (poem != null)
                                                        {
                                                            narration.AudioTitle = poem.Title;
                                                        }
                                                    }
                                                    context.Recitations.Add(narration);
                                                }

                                                session.UploadedFiles.Where(f => f.Id == file.Id).SingleOrDefault().ProcessResultMsg = "";
                                                session.UploadedFiles.Where(f => f.Id == file.Id).SingleOrDefault().ProcessResult = true;
                                                session.UploadedFiles.Where(f => f.Id == mp3file.Id).SingleOrDefault().ProcessResultMsg = "";
                                                session.UploadedFiles.Where(f => f.Id == mp3file.Id).SingleOrDefault().ProcessResult = true;
                                            }
                                        }
                                        
                                        await context.SaveChangesAsync();

                                      
                                    }
                                }
                                catch (Exception exp)
                                {
                                    session.UploadedFiles.Where(f => f.Id == file.Id).SingleOrDefault().ProcessResultMsg = "خطا در پس پردازش فایل. اطلاعات بیشتر: " + exp.ToString();
                                    context.UploadSessions.Update(session);
                                    await context.SaveChangesAsync();
                                }
                                processFilesCount++;
                                session.ProcessProgress = (int)(processFilesCount / fileCount * 100.0);
                            }

                            session.ProcessEndTime = DateTime.Now;
                            context.Update(session);

                            //remove session files (house keeping)
                            foreach (UploadSessionFile file in session.UploadedFiles)
                            {
                                if(!file.ProcessResult && string.IsNullOrEmpty(file.ProcessResultMsg))
                                {
                                    file.ProcessResultMsg = "فایل xml یا mp3 متناظر این فایل یافت نشد.";
                                    context.Update(file);
                                   
                                }
                                if(File.Exists(file.FilePath))
                                {
                                    try
                                    {
                                        File.Delete(file.FilePath);
                                    }
                                    catch
                                    {
                                        //there should be a house keeping process somewhere to handle undeletable files
                                    }
                                }
                            }
                            await context.SaveChangesAsync();

                            await _notificationService.PushNotification
                            (
                                session.UseId,
                                "پایان پردازش خوانش بارگذاری شده",
                                $"پردازش خوانشهای بارگذاری شدهٔ اخیر شما تکمیل شده است.{Environment.NewLine}" +
                                $"می‌توانید با مراجعه به این صفحه TODO: client url وضعیت آنها را بررسی و ذر صورت عدم وجود خطا تقاضای بررسی آنها توسط ناظران را ثبت کنید."
                            );
                        }
                       
                    }
                    );

                   

                return new RServiceResult<UploadSession>(session);

            }
            catch (Exception exp)
            {
                return new RServiceResult<UploadSession>(null, exp.ToString());
            }
        }

        /// <summary>
        /// Moderate pending narration
        /// </summary>
        /// <param name="id"></param>
        /// <param name="moderatorId"></param>
        /// <param name="model"></param>
        /// <returns></returns>
        public async Task<RServiceResult<RecitationViewModel>> ModeratePoemNarration(int id, Guid moderatorId, RecitationModerateViewModel model)
        {
            try
            {
                Recitation narration = await _context.Recitations.Include(a => a.Owner).Where(a => a.Id == id).SingleOrDefaultAsync();
                if (narration == null)
                    return new RServiceResult<RecitationViewModel>(null, "404");
                if (narration.ReviewStatus != AudioReviewStatus.Draft && narration.ReviewStatus != AudioReviewStatus.Pending)
                    return new RServiceResult<RecitationViewModel>(null, "خوانش می‌بایست در وضعیت پیش‌نویس یا در انتظار بازبینی باشد.");
                narration.ReviewDate = DateTime.Now;
                narration.ReviewerId = moderatorId;
                if(model.Result != PoemNarrationModerationResult.MetadataNeedsFixation)
                {
                    narration.ReviewStatus = model.Result == PoemNarrationModerationResult.Approve ? AudioReviewStatus.Approved : AudioReviewStatus.Rejected;
                }
                if (narration.ReviewStatus == AudioReviewStatus.Rejected)
                {
                    narration.AudioSyncStatus = (int)AudioSyncStatus.SynchronizedOrRejected;
                    //TODO: delete rejected items files passed a certain period of time in a maintenance job
                }
                narration.ReviewMsg = model.Message;
                _context.Recitations.Update(narration);
                await _context.SaveChangesAsync();

                if (model.Result == PoemNarrationModerationResult.MetadataNeedsFixation)
                {
                    await _notificationService.PushNotification
                         (
                             narration.OwnerId,
                             "نیاز به بررسی خوانش ارسالی",
                             $"خوانش شما بررسی شده و نیاز به اعمال تغییرات دارد.{Environment.NewLine}" +
                             $"می‌توانید با مراجعه به این صفحه TODO: client url وضعیت آن را بررسی کنید."
                         );
                }
                else
                if (narration.ReviewStatus == AudioReviewStatus.Rejected) 
                {
                    await _notificationService.PushNotification
                         (
                             narration.OwnerId,
                             "عدم پذیرش خوانش ارسالی",
                             $"خوانش ارسالی شما قابل پذیرش نبود.{Environment.NewLine}" +
                             $"می‌توانید با مراجعه به این صفحه TODO: client url وضعیت آن را بررسی کنید."
                         );
                }
                else //approved:
                {
                    _backgroundTaskQueue.QueueBackgroundWorkItem
                    (
                    async token =>
                    {
                        using (RMuseumDbContext context = new RMuseumDbContext(Configuration)) //this is long running job, so _context might be already been freed/collected by GC
                        {
                            await _PublishNarration(narration, context, false);
                        }
                    });
                }

                


                return new RServiceResult<RecitationViewModel>(new RecitationViewModel(narration, narration.Owner, await _context.GanjoorPoems.Where(p => p.Id == narration.GanjoorPostId).SingleOrDefaultAsync()));
            }
            catch (Exception exp)
            {
                return new RServiceResult<RecitationViewModel>(null, exp.ToString());
            }
        }

       

        private async Task _PublishNarration(Recitation narration, RMuseumDbContext context, bool replace)
        {
            RecitationPublishingTracker tracker = new RecitationPublishingTracker()
            {
                PoemNarrationId = narration.Id,
                StartDate = DateTime.Now,
                XmlFileCopied = false,
                Mp3FileCopied = false,
                FirstDbUpdated = false,
                SecondDbUpdated = false,
            };
            context.RecitationPublishingTrackers.Add(tracker);
            await context.SaveChangesAsync();
           
            using var client = new SftpClient
                        (
                            Configuration.GetSection("AudioSFPServer")["Host"],
                            int.Parse(Configuration.GetSection("AudioSFPServer")["Port"]),
                            Configuration.GetSection("AudioSFPServer")["Username"],
                            Configuration.GetSection("AudioSFPServer")["Password"]
                            );
            try
            {
                client.Connect();

                using var x = File.OpenRead(narration.LocalXmlFilePath);
                client.UploadFile(x, $"{Configuration.GetSection("AudioSFPServer")["RootPath"]}{narration.RemoteXMLFilePath}", true);

                tracker.XmlFileCopied = true;
                context.RecitationPublishingTrackers.Update(tracker);
                await context.SaveChangesAsync();

                using var s = File.OpenRead(narration.LocalMp3FilePath);
                client.UploadFile(s, $"{Configuration.GetSection("AudioSFPServer")["RootPath"]}{narration.RemoteMp3FilePath}", true);

                tracker.Mp3FileCopied = true;
                context.RecitationPublishingTrackers.Update(tracker);
                await context.SaveChangesAsync();


                if(!replace)
                {
                    string sql = $"INSERT INTO ganja_gaudio (audio_post_ID,audio_order,audio_xml,audio_ogg,audio_mp3,audio_title,audio_artist," +
                    $"audio_artist_url,audio_src,audio_src_url, audio_guid, audio_fchecksum, audio_mp3bsize, audio_oggbsize, audio_date) VALUES " +
                    $"({narration.GanjoorPostId},{narration.AudioOrder},'{narration.RemoteXMLFilePath}', '', '{narration.Mp3Url}', '{narration.AudioTitle}', '{narration.AudioArtist}', " +
                    $"'{narration.AudioArtistUrl}', '{narration.AudioSrc}', '{narration.AudioSrcUrl}', '{narration.LegacyAudioGuid}', '{narration.Mp3FileCheckSum}', {narration.Mp3SizeInBytes}, 0, NOW())";

                    using (MySqlConnection connection = new MySqlConnection
                    (
                    $"server={Configuration.GetSection("AudioMySqlServer")["Server"]};uid={Configuration.GetSection("AudioMySqlServer")["Username"]};pwd={Configuration.GetSection("AudioMySqlServer")["Password"]};database={Configuration.GetSection("AudioMySqlServer")["Database"]};charset=utf8"
                    ))
                    {
                        await connection.OpenAsync();
                        await connection.ExecuteAsync(sql);
                    }

                    tracker.FirstDbUpdated = true;
                    context.RecitationPublishingTrackers.Update(tracker);
                    await context.SaveChangesAsync();

                    //We are using two database for different purposes on the remote
                    using (MySqlConnection connection = new MySqlConnection
                    (
                    $"server={Configuration.GetSection("AudioMySqlServer")["Server"]};uid={Configuration.GetSection("AudioMySqlServer")["2ndUsername"]};pwd={Configuration.GetSection("AudioMySqlServer")["2ndPassword"]};database={Configuration.GetSection("AudioMySqlServer")["2ndDatabase"]};charset=utf8"
                    ))
                    {
                        await connection.OpenAsync();
                        await connection.ExecuteAsync(sql);
                    }

                    tracker.SecondDbUpdated = true;
                    context.RecitationPublishingTrackers.Update(tracker);
                    await context.SaveChangesAsync();
                }

               

                narration.AudioSyncStatus = (int)AudioSyncStatus.SynchronizedOrRejected;
                context.Recitations.Update(narration);
                await context.SaveChangesAsync();



                await _notificationService.PushNotification
                (
                    narration.OwnerId,
                    "انتشار خوانش ارسالی",
                    $"خوانش ارسالی شما منتشر شد.{Environment.NewLine}" +
                    $"می‌توانید با مراجعه به این صفحه TODO: client url وضعیت آن را بررسی کنید."
                );

                tracker.Finished = true;
                tracker.FinishDate = DateTime.Now;
                context.RecitationPublishingTrackers.Update(tracker);
                await context.SaveChangesAsync();


            }
            catch(Exception exp)
            {
                //if an error occurs, narration.AudioSyncStatus is not updated and narration can be idetified later to do "retrypublish" attempts  
                tracker.LastException = exp.ToString();
                context.RecitationPublishingTrackers.Update(tracker);
                await context.SaveChangesAsync();
            }
            finally
            {
                client.Disconnect();
            }
        }

        /// <summary>
        /// retry publish unpublished narrations
        /// </summary>
        public void RetryPublish()
        {
            _backgroundTaskQueue.QueueBackgroundWorkItem
                    (
                    async token =>
                    {
                        using (RMuseumDbContext context = new RMuseumDbContext(Configuration)) //this is long running job, so _context might be already been freed/collected by GC
                        {
                            var list = await context.Recitations.Where(a => a.ReviewStatus == AudioReviewStatus.Approved && a.AudioSyncStatus != (int)AudioSyncStatus.SynchronizedOrRejected).ToListAsync();
                            foreach (Recitation narration in list)
                            {
                                await _PublishNarration(narration, context, false);
                            }
                        }
                    });
        }


        /// <summary>
        /// Get Upload Session (including files)
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public async Task<RServiceResult<UploadSession>> GetUploadSession(Guid id)
        {
            try
            {
                return new RServiceResult<UploadSession>
                    (
                    await _context.UploadSessions.Include(s => s.UploadedFiles).FirstOrDefaultAsync(s => s.Id == id)
                    );

            }
            catch (Exception exp)
            {
                return new RServiceResult<UploadSession>(null, exp.ToString());
            }
        }

        /// <summary>
        /// Get User Profiles
        /// </summary>
        /// <param name="userId"></param>
        /// <returns></returns>
        public async Task<RServiceResult<UserRecitationProfileViewModel[]>> GetUserNarrationProfiles(Guid userId)
        {
            try
            {
                List<UserRecitationProfileViewModel> profiles = new List<UserRecitationProfileViewModel>();
                
                foreach(UserRecitationProfile p in (await _context.UserRecitationProfiles.Include(p => p.User).Where(p => p.UserId == userId).ToArrayAsync()))
                {
                    profiles.Add(new UserRecitationProfileViewModel(p));
                }
                return new RServiceResult<UserRecitationProfileViewModel[]>(profiles.ToArray());

            }
            catch (Exception exp)
            {
                return new RServiceResult<UserRecitationProfileViewModel[]>(null, exp.ToString());
            }
        }

        /// <summary>
        /// validating narration profile
        /// </summary>
        /// <param name="p"></param>
        /// <returns></returns>
        private static string GetUserProfileValidationError(UserRecitationProfile p)
        {
            if(string.IsNullOrEmpty(p.Name))
            {
                return "نام نمایه نباید خالی باشد.";
            }
            if (p.ArtistName.Length < 3)
            {
                return "نام خوانشگر باید حداقل شامل سه نویسه باشد.";
            }

            if (p.FileSuffixWithoutDash.Length < 2)
            {
                return "طول پسوند قابل پذیرش حداقل دو کاراکتر است.";
            }

            if (p.FileSuffixWithoutDash.Length > 4)
            {
                return "طول پسوند قابل پذیرش حداکثر چهار کاراکتر است.";
            }

            string s = LanguageUtils.GetFirstNotMatchingCharacter(p.ArtistName, LanguageUtils.PersianAlphabet, " ‌");
            if (s != "")
            {
                return  $"نام خوانشگر فقط باید شامل حروف فارسی و فاصله باشد. اولین حرف غیرمجاز = {s}";
            }

            s = LanguageUtils.GetFirstNotMatchingCharacter(p.ArtistUrl, LanguageUtils.EnglishLowerCaseAlphabet, LanguageUtils.EnglishLowerCaseAlphabet.ToUpper() + @":/._-0123456789%");

            if(!string.IsNullOrEmpty(p.ArtistUrl))
            {
                bool result = Uri.TryCreate(p.ArtistUrl, UriKind.Absolute, out Uri uriResult)
                   && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
                if (!result)
                {
                    return $"نشانی وب خوانشگر نامعتبر است.";
                }
            }
            

            s = LanguageUtils.GetFirstNotMatchingCharacter(p.AudioSrcUrl, LanguageUtils.EnglishLowerCaseAlphabet, LanguageUtils.EnglishLowerCaseAlphabet.ToUpper() + @":/._-0123456789%");
            
            if(!string.IsNullOrEmpty(p.AudioSrcUrl))
            {
                bool result = Uri.TryCreate(p.AudioSrcUrl, UriKind.Absolute, out Uri uriResult)
                   && (uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps);
                if (!result)
                {
                    return $"نشانی وب منبع نامعتبر است.";
                }
            }
            
            s = LanguageUtils.GetFirstNotMatchingCharacter(p.FileSuffixWithoutDash, LanguageUtils.EnglishLowerCaseAlphabet);

            if (s != "")
            {
                return $"پسوند فقط می‌تواند از حروف کوچک انگلیسی تشکیل شود. اولین حرف غیر مجاز = {s}";
            }

            return "";
        }




        /// <summary>
        /// Add a narration profile
        /// </summary>
        /// <param name="profile"></param>
        /// <returns></returns>
        public async Task<RServiceResult<UserRecitationProfileViewModel>> AddUserNarrationProfiles(UserRecitationProfileViewModel profile)
        {
            try
            {
                var p = new UserRecitationProfile()
                {
                    UserId = profile.UserId,
                    Name = profile.Name.Trim(),
                    ArtistName = profile.ArtistName.Trim(),
                    ArtistUrl = profile.ArtistUrl.Trim(),
                    AudioSrc = profile.AudioSrc.Trim(),
                    AudioSrcUrl = profile.AudioSrcUrl.Trim(),
                    FileSuffixWithoutDash = profile.FileSuffixWithoutDash.Trim(),
                    IsDefault = profile.IsDefault
                };

                string error = GetUserProfileValidationError(p);
                if(error != "")
                {
                    return new RServiceResult<UserRecitationProfileViewModel>(null, error);
                }

                if((await _context.UserRecitationProfiles.Where(e => e.UserId == p.Id && e.Name == p.Name).SingleOrDefaultAsync())!=null)
                {
                    return new RServiceResult<UserRecitationProfileViewModel>(null, "شما نمایهٔ دیگری با همین نام دارید.");
                }

                await _context.UserRecitationProfiles.AddAsync(p);
                   
                await _context.SaveChangesAsync();
                if(p.IsDefault)
                {
                    foreach(var o in _context.UserRecitationProfiles.Where(o => o.Id != p.Id && o.IsDefault).Select(o => o))
                    {
                        o.IsDefault = false;
                        _context.UserRecitationProfiles.Update(o);
                    }
                    await _context.SaveChangesAsync();
                }
                return new RServiceResult<UserRecitationProfileViewModel>(new UserRecitationProfileViewModel(p));
            }
            catch (Exception exp)
            {
                return new RServiceResult<UserRecitationProfileViewModel>(null, exp.ToString());
            }
        }

        /// <summary>
        /// Update a narration profile 
        /// </summary>
        /// <param name="profile"></param>
        /// <returns></returns>
        public async Task<RServiceResult<UserRecitationProfileViewModel>> UpdateUserNarrationProfiles(UserRecitationProfileViewModel profile)
        {
            try
            {

                var p = await _context.UserRecitationProfiles.Where(p => p.Id == profile.Id).SingleOrDefaultAsync();

                if (p.UserId != profile.UserId)
                    return new RServiceResult<UserRecitationProfileViewModel>(null, "permission error");

                p.Name = profile.Name.Trim();
                p.ArtistName = profile.ArtistName.Trim();
                p.ArtistUrl = profile.ArtistUrl.Trim();
                p.AudioSrc = profile.AudioSrc.Trim();
                p.AudioSrcUrl = profile.AudioSrcUrl.Trim();
                p.FileSuffixWithoutDash = profile.FileSuffixWithoutDash.Trim();
                p.IsDefault = profile.IsDefault;

                string error = GetUserProfileValidationError(p);
                if (error != "")
                {
                    return new RServiceResult<UserRecitationProfileViewModel>(null, error);
                }

                if ((await _context.UserRecitationProfiles.Where(e => e.UserId == p.Id && e.Name == p.Name && e.Id != p.Id).SingleOrDefaultAsync()) != null)
                {
                    return new RServiceResult<UserRecitationProfileViewModel>(null, "شما نمایهٔ دیگری با همین نام دارید.");
                }

                _context.UserRecitationProfiles.Update(p);

                await _context.SaveChangesAsync();
                if (p.IsDefault)
                {
                    foreach (var o in _context.UserRecitationProfiles.Where(o => o.Id != p.Id && o.IsDefault).Select(o => o))
                    {
                        o.IsDefault = false;
                        _context.UserRecitationProfiles.Update(o);
                    }
                    await _context.SaveChangesAsync();
                }
                return new RServiceResult<UserRecitationProfileViewModel>(new UserRecitationProfileViewModel(p));
            }
            catch (Exception exp)
            {
                return new RServiceResult<UserRecitationProfileViewModel>(null, exp.ToString());
            }
        }

        /// <summary>
        /// Delete a narration profile 
        /// </summary>
        /// <param name="id"></param>
        /// <param name="userId"></param>
        /// <returns></returns>
        public async Task<RServiceResult<bool>> DeleteUserNarrationProfiles(Guid id, Guid userId)
        {
            try
            {

                var p = await _context.UserRecitationProfiles.Where(p => p.Id == id).SingleOrDefaultAsync();

                if (p.UserId != userId)
                    return new RServiceResult<bool>(false);

                _context.UserRecitationProfiles.Remove(p);

                await _context.SaveChangesAsync();
                
                return new RServiceResult<bool>(true);
            }
            catch (Exception exp)
            {
                return new RServiceResult<bool>(false, exp.ToString());
            }
        }

        /// <summary>
        /// Get uploads descending by upload time
        /// </summary>
        /// <param name="paging"></param>
        /// <param name="userId">if userId is empty all user uploads would be returned</param>
        /// <returns></returns>
        public async Task<RServiceResult<(PaginationMetadata PagingMeta, UploadedItemViewModel[] Items)>> GetUploads(PagingParameterModel paging, Guid userId)
        {
            try
            {
                var source =
                    (
                    from file in _context.UploadedFiles
                    join session in _context.UploadSessions.Include(s => s.User)
                    on file.UploadSessionId equals session.Id
                    where userId == Guid.Empty || session.UseId == userId
                    orderby session.UploadEndTime descending
                    select new UploadedItemViewModel()
                    { FileName = file.FileName, ProcessResult = file.ProcessResult, ProcessResultMsg = file.ProcessResultMsg, UploadEndTime = session.UploadEndTime, UserName = session.User.UserName, ProcessStartTime = session.ProcessStartTime, ProcessProgress = session.ProcessProgress, ProcessEndTime = session.ProcessEndTime }
                    ).AsQueryable();
                    
                (PaginationMetadata PagingMeta, UploadedItemViewModel[] Items) paginatedResult =
                    await QueryablePaginator<UploadedItemViewModel>.Paginate(source, paging);

               

                return new RServiceResult<(PaginationMetadata PagingMeta, UploadedItemViewModel[] Items)>((paginatedResult.PagingMeta, paginatedResult.Items));
            }
            catch (Exception exp)
            {
                return new RServiceResult<(PaginationMetadata PagingMeta, UploadedItemViewModel[] Items)>((PagingMeta: null, Items: null), exp.ToString());
            }
        }

        /// <summary>
        /// publishing tracker data
        /// </summary>
        /// <param name="paging"></param>
        /// <param name="inProgress"></param>
        /// <param name="finished"></param>
        /// <returns></returns>
        public async Task<RServiceResult<(PaginationMetadata PagingMeta, RecitationPublishingTracker[] Items)>> GetPublishingQueueStatus(PagingParameterModel paging, bool inProgress, bool finished)
        {
            try
            {
                var source =
                     from tracker in _context.RecitationPublishingTrackers
                     .Include(t => t.PoemNarration)
                     .Where(a =>
                            (inProgress && !a.Finished)
                            ||
                            (finished && a.Finished)
                            )
                    .OrderByDescending(a => a.StartDate)
                     select tracker;

                (PaginationMetadata PagingMeta, RecitationPublishingTracker[] Items) paginatedResult =
                    await QueryablePaginator<RecitationPublishingTracker>.Paginate(source, paging);

                return new RServiceResult<(PaginationMetadata PagingMeta, RecitationPublishingTracker[] Items)>(paginatedResult);
            }
            catch (Exception exp)
            {
                return new RServiceResult<(PaginationMetadata PagingMeta, RecitationPublishingTracker[] Items)>((PagingMeta: null, Items: null), exp.ToString());
            }
        }

        /// <summary>
        /// Configuration
        /// </summary>
        protected IConfiguration Configuration { get; }

        /// <summary>
        /// Database Contetxt
        /// </summary>
        protected readonly RMuseumDbContext _context;

        /// <summary>
        /// Background Task Queue Instance
        /// </summary>
        protected readonly IBackgroundTaskQueue _backgroundTaskQueue;

        /// <summary>
        /// Messaging service
        /// </summary>
        protected readonly IRNotificationService _notificationService;


        /// <summary>
        /// constructor
        /// </summary>
        /// <param name="context"></param>
        /// <param name="configuration"></param>
        /// <param name="backgroundTaskQueue"></param>
        /// <param name="notificationService"></param>
        public RecitationService(RMuseumDbContext context, IConfiguration configuration, IBackgroundTaskQueue backgroundTaskQueue, IRNotificationService notificationService)
        {
            _context = context;
            Configuration = configuration;
            _backgroundTaskQueue = backgroundTaskQueue;
            _notificationService = notificationService;
        }
    }
}