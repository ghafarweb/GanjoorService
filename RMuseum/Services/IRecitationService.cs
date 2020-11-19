﻿using Microsoft.AspNetCore.Http;
using RMuseum.Models.GanjoorAudio;
using RMuseum.Models.GanjoorAudio.ViewModels;
using RMuseum.Models.UploadSession;
using RMuseum.Models.UploadSession.ViewModels;
using RSecurityBackend.Models.Generic;
using System;
using System.Threading.Tasks;

namespace RMuseum.Services
{
    /// <summary>
    /// Audio Narration Service
    /// </summary>
    public interface IRecitationService
    {
        /// <summary>
        /// returns list of narrations
        /// </summary>
        /// <param name="paging"></param>
        /// <param name="filteredUserId">send Guid.Empty if you want all narrations</param>
        /// <param name="status"></param>
        /// <returns></returns>
        public Task<RServiceResult<(PaginationMetadata PagingMeta, RecitationViewModel[] Items)>> GetAll(PagingParameterModel paging, Guid filteredUserId, AudioReviewStatus status);

        /// <summary>
        /// return selected narration information
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        Task<RServiceResult<RecitationViewModel>> Get(int id);

        /// <summary>
        /// Gets Verse Sync Range Information
        /// </summary>
        /// <param name="id">narration id</param>
        /// <returns></returns>
        Task<RServiceResult<RecitationVerseSync[]>> GetPoemNarrationVerseSyncArray(int id);

        /// <summary>
        /// updates metadata for narration
        /// </summary>
        /// <param name="id"></param>
        /// <param name="metadata"></param>
        /// <returns></returns>
        Task<RServiceResult<RecitationViewModel>> UpdatePoemNarration(int id, RecitationViewModel metadata);

        /// <summary>
        /// imports narration data from ganjoor MySql database
        /// </summary>
        /// <param name="ownerRAppUserId">User Id which becomes owner of imported data</param>
        /// <returns></returns>
        Task<RServiceResult<bool>> OneTimeImport(Guid ownerRAppUserId);

        /// <summary>
        /// build profiles from exisng narrations data
        /// </summary>
        /// <param name="ownerRAppUserId">User Id which becomes owner of imported data</param>
        /// <returns>error string if occurs</returns>
        Task<string> BuildProfilesFromExistingData(Guid ownerRAppUserId);

        /// <summary>
        /// Initiate New Upload Session for audio
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="replace"></param>
        /// <returns></returns>
        Task<RServiceResult<UploadSession>> InitiateNewUploadSession(Guid userId, bool replace);

        /// <summary>
        /// Save uploaded file
        /// </summary>
        /// <param name="uploadedFile"></param>
        /// <returns></returns>
        Task<RServiceResult<UploadSessionFile>> SaveUploadedFile(IFormFile uploadedFile);

        /// <summary>
        /// finalize upload session (add files)
        /// </summary>
        /// <param name="session"></param>
        /// <param name="files"></param>
        /// <returns></returns>
        Task<RServiceResult<UploadSession>> FinalizeNewUploadSession(UploadSession session, UploadSessionFile[] files);

        /// <summary>
        /// Moderate pending narration
        /// </summary>
        /// <param name="id"></param>
        /// <param name="moderatorId"></param>
        /// <param name="model"></param>
        /// <returns></returns>
        Task<RServiceResult<RecitationViewModel>> ModeratePoemNarration(int id, Guid moderatorId, RecitationModerateViewModel model);

        /// <summary>
        /// Get Upload Session (including files)
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        Task<RServiceResult<UploadSession>> GetUploadSession(Guid id);

        /// <summary>
        /// Get User Profiles
        /// </summary>
        /// <param name="userId"></param>
        /// <returns></returns>
        Task<RServiceResult<UserRecitationProfileViewModel[]>> GetUserNarrationProfiles(Guid userId);

        /// <summary>
        /// Add a narration profile
        /// </summary>
        /// <param name="profile"></param>
        /// <returns></returns>
        Task<RServiceResult<UserRecitationProfileViewModel>> AddUserNarrationProfiles(UserRecitationProfileViewModel profile);

        /// <summary>
        /// Update a narration profile 
        /// </summary>
        /// <param name="profile"></param>
        /// <returns></returns>
        Task<RServiceResult<UserRecitationProfileViewModel>> UpdateUserNarrationProfiles(UserRecitationProfileViewModel profile);

        /// <summary>
        /// Delete a narration profile 
        /// </summary>
        /// <param name="id"></param>
        /// <param name="userId"></param>
        /// <returns></returns>
        Task<RServiceResult<bool>> DeleteUserNarrationProfiles(Guid id, Guid userId);

        /// <summary>
        /// Get uploads descending by upload time
        /// </summary>
        /// <param name="paging"></param>
        /// <param name="userId">if userId is empty all user uploads would be returned</param>
        /// <returns></returns>
        Task<RServiceResult<(PaginationMetadata PagingMeta, UploadedItemViewModel[] Items)>> GetUploads(PagingParameterModel paging, Guid userId);

        /// <summary>
        /// retry publish unpublished narrations
        /// </summary>
        public void RetryPublish();

        /// <summary>
        /// publishing tracker data
        /// </summary>
        /// <param name="paging"></param>
        /// <param name="inProgress"></param>
        /// <param name="finished"></param>
        /// <returns></returns>
        Task<RServiceResult<(PaginationMetadata PagingMeta, RecitationPublishingTracker[] Items)>> GetPublishingQueueStatus(PagingParameterModel paging, bool inProgress, bool finished);
    }
}