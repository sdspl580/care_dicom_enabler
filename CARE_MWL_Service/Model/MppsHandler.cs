//// Copyright (c) 2012-2022 fo-dicom contributors.
//// Licensed under the Microsoft Public License (MS-PL).

//using System.Collections.Generic;
//using System.Linq;
//using FellowOakDicom.Log;

//namespace Worklist_SCP.Model
//{

//    /// <summary>
//    /// An implementation of IMppsSource, that does only logging but does not store the MPPS messages
//    /// </summary>
//    class MppsHandler : IMppsSource
//    {

//        public static Dictionary<string, WorklistItem> PendingProcedures { get; } = new Dictionary<string, WorklistItem>();

//        private readonly ILogger _logger;


//        public MppsHandler(ILogger logger)
//        {
//            _logger = logger;
//        }


//        public bool SetInProgress(string sopInstanceUID, string procedureStepId)
//        {
//            var workItem = WorklistServer.CurrentWorklistItems
//                .FirstOrDefault(w => w.ProcedureStepID == procedureStepId);
//            if (workItem == null)
//            {
//                // the procedureStepId provided cannot be found any more, so the data is invalid or the 
//                // modality tries to start a procedure that has been deleted/changed on the ris side...
//                return false;
//            }

//            // now here change the sate of the procedure in the database or do similar stuff...
//            _logger.Info($"Procedure with id {workItem.ProcedureStepID} of Patient {workItem.Surname} {workItem.Forename} is started");

//            // remember the sopInstanceUID and store the worklistitem to which the sopInstanceUID belongs. 
//            // You should do this more permanent like in database or in file
//            PendingProcedures.Add(sopInstanceUID, workItem);
//            return true;
//        }


//        public bool SetDiscontinued(string sopInstanceUID, string reason)
//        {
//            if (!PendingProcedures.ContainsKey(sopInstanceUID))
//            {
//                // there is no pending procedure with this sopInstanceUID!
//                return false;
//            }
//            var workItem = PendingProcedures[sopInstanceUID];

//            // now here change the sate of the procedure in the database or do similar stuff...
//            _logger.Info($"Procedure with id {workItem.ProcedureStepID} of Patient {workItem.Surname} {workItem.Forename} is discontinued for reason {reason}");

//            // since the procedure was stopped, we remove it from the list of pending procedures
//            PendingProcedures.Remove(sopInstanceUID);
//            return true;
//        }


//        public bool SetCompleted(string sopInstanceUID, string doseDescription, List<string> affectedInstanceUIDs)
//        {
//            if (!PendingProcedures.ContainsKey(sopInstanceUID))
//            {
//                // there is no pending procedure with this sopInstanceUID!
//                return false;
//            }
//            var workItem = PendingProcedures[sopInstanceUID];

//            // now here change the sate of the procedure in the database or do similar stuff...
//            _logger.Info($"Procedure with id {workItem.ProcedureStepID} of Patient {workItem.Surname} {workItem.Forename} is completed");

//            // the MPPS completed message contains some additional informations about the performed procedure.
//            // this informations are very vendor depending, so read the DICOM Conformance Statement or read
//            // the DICOM logfiles to see which informations the vendor sends

//            // since the procedure was completed, we remove it from the list of pending procedures
//            PendingProcedures.Remove(sopInstanceUID);
//            return true;
//        }


//    }
//}


// Copyright (c) 2012-2022 fo-dicom contributors.
// Licensed under the Microsoft Public License (MS-PL).

using System.Collections.Generic;
using System.IO;
using System.Linq;
using FellowOakDicom.Log;
using Plexus.Common.Database;
using Serilog;
using Worklist_SCP.Services;
using static System.Net.Mime.MediaTypeNames;

namespace Worklist_SCP.Model
{
    /// <summary>
    /// Handles MPPS status changes and updates the external API.
    /// </summary>
    class MppsHandler : IMppsSource
    {
        /// <summary>
        /// Stores MPPS procedures that are currently in progress.
        /// 
        /// NOTE:
        /// This is an in-memory collection.
        /// For production, consider storing this information in the database.
        /// </summary>
        public static Dictionary<string, WorklistItem> PendingProcedures
            = new Dictionary<string, WorklistItem>();

        private readonly Serilog.ILogger _logger;
        //public static Serilog.ILogger _logger = null;
        private readonly MppsApiService _mppsApiService;

        public MppsHandler(Serilog.ILogger logger)
        {

            _logger = logger;
            //_logger = GetFileLogger();
            // API service used to update MPPS status
            _mppsApiService = new MppsApiService();
        //    fileLogger = GetFileLogger();
        }



        //private Serilog.ILogger GetFileLogger()
        //{
        //    return new LoggerConfiguration().
        //        WriteTo.File("logs/ModalitySCPaa.txt",
        //        restrictedToMinimumLevel: Serilog.Events.LogEventLevel.Information,
        //        shared: true,
        //        retainedFileCountLimit: 3,
        //        rollOnFileSizeLimit: true,
        //        fileSizeLimitBytes: 4048)
        //        .CreateLogger();
        //}

        //public static Serilog.ILogger fileLogger = null;
        /// <summary>
        /// MPPS N-CREATE:
        /// Procedure is started and status is IN PROGRESS.
        /// </summary>
        public bool SetInProgress(
            string sopInstanceUID,
            string procedureStepId,string accessionNumber)
        {
            try
            {
                _logger.Information(
                    $"MPPS N-CREATE received. " +
                    $"SOP Instance UID={sopInstanceUID}, " +
                    $"ProcedureStepID={procedureStepId}");

                // Find the corresponding MWL worklist item
                var workItem = WorklistServer.CurrentWorklistItems
                    .FirstOrDefault(
                        w => w.ProcedureStepID == procedureStepId);

                if (workItem == null)
                {
                    _logger.Error(
                        $"MPPS procedure step not found in MWL. " +
                        $"ProcedureStepID={procedureStepId}");

                    return false;
                }

                _logger.Information(
                    $"Procedure with ID {workItem.ProcedureStepID} " +
                    $"of Patient {workItem.Surname} " +
                    $"{workItem.Forename} is started.");


                // Store the MPPS SOP Instance UID
                // so that the later N-SET can find this procedure.
                if (PendingProcedures.ContainsKey(sopInstanceUID))
                {
                    _logger.Information(
                        $"MPPS SOP Instance UID already exists: " +
                        $"{sopInstanceUID}");

                    return false;
                }

                PendingProcedures.Add(
                    sopInstanceUID,
                    workItem);


                // ---------------------------------------------------------
                // CALL EXTERNAL API
                // ---------------------------------------------------------

                bool apiResult =
                    _mppsApiService
                        .UpdateMppsStatusAsync(
                            procedureStepId,
                            sopInstanceUID,
                            "IN PROGRESS", accessionNumber)
                        .GetAwaiter()
                        .GetResult();


                if (!apiResult)
                {
                    _logger.Error(
                        $"Failed to update MPPS status through API. " +
                        $"ProcedureStepID={procedureStepId}, " +
                        $"MPPS UID={sopInstanceUID}, " +
                        $"Status=IN PROGRESS");

                    // Remove from memory because API update failed
                    PendingProcedures.Remove(sopInstanceUID);

                    return false;
                }


                _logger.Information(
                    $"MPPS API successfully updated. " +
                    $"ProcedureStepID={procedureStepId}, " +
                    $"Status=IN PROGRESS");

                return true;
            }
            catch (System.Exception ex)
            {
                _logger.Error(
                    $"Exception while processing MPPS IN PROGRESS. " +
                    $"SOP Instance UID={sopInstanceUID}. " +
                    $"Error={ex.Message}");

                return false;
            }
        }


        /// <summary>
        /// MPPS N-SET:
        /// Procedure was discontinued.
        /// </summary>
        public bool SetDiscontinued(
            string sopInstanceUID,
            string reason,string accessionNumber)
        {
            try
            {
                _logger.Information(
                    $"MPPS N-SET DISCONTINUED received. " +
                    $"SOP Instance UID={sopInstanceUID}");

                // Find the previously created MPPS procedure
                if (!PendingProcedures.ContainsKey(sopInstanceUID))
                {
                    _logger.Error(
                        $"No pending MPPS procedure found for SOP Instance UID " +
                        $"{sopInstanceUID}");

                    return false;
                }

                var workItem =
                    PendingProcedures[sopInstanceUID];


                _logger.Information(
                    $"Procedure with ID {workItem.ProcedureStepID} " +
                    $"of Patient {workItem.Surname} " +
                    $"{workItem.Forename} is discontinued. " +
                    $"Reason={reason}");


                // ---------------------------------------------------------
                // CALL EXTERNAL API
                // ---------------------------------------------------------

                bool apiResult =
                    _mppsApiService
                        .UpdateMppsStatusAsync(
                            workItem.ProcedureStepID,
                            sopInstanceUID, accessionNumber,
                            "DISCONTINUED")
                        .GetAwaiter()
                        .GetResult();


                if (!apiResult)
                {
                    _logger.Error(
                        $"Failed to update MPPS status through API. " +
                        $"ProcedureStepID={workItem.ProcedureStepID}, " +
                        $"MPPS UID={sopInstanceUID}, " +
                        $"Status=DISCONTINUED");

                    // Keep the pending procedure
                    // so it can be retried.
                    return false;
                }


                // API update succeeded.
                // Remove from pending procedures.
                PendingProcedures.Remove(sopInstanceUID);


                _logger.Information(
                    $"MPPS API successfully updated. " +
                    $"ProcedureStepID={workItem.ProcedureStepID}, " +
                    $"Status=DISCONTINUED");

                return true;
            }
            catch (System.Exception ex)
            {
                _logger.Error(
                    $"Exception while processing MPPS DISCONTINUED. " +
                    $"SOP Instance UID={sopInstanceUID}. " +
                    $"Error={ex.Message}");

                return false;
            }
        }


        /// <summary>
        /// MPPS N-SET:
        /// Procedure was completed.
        /// </summary>
        public bool SetCompleted(
            string sopInstanceUID,
            string doseDescription,
            List<string> affectedInstanceUIDs,string accessionNumber)
        {
            try
            {
                _logger.Information(
                    $"MPPS N-SET COMPLETED received. " +
                    $"SOP Instance UID={sopInstanceUID}");


                // Find the previously created MPPS procedure
                if (!PendingProcedures.ContainsKey(sopInstanceUID))
                {
                    _logger.Error(
                        $"No pending MPPS procedure found for SOP Instance UID " +
                        $"{sopInstanceUID}");

                    return false;
                }

                var workItem =
                    PendingProcedures[sopInstanceUID];


                _logger.Information(
                    $"Procedure with ID {workItem.ProcedureStepID} " +
                    $"of Patient {workItem.Surname} " +
                    $"{workItem.Forename} is completed.");


                // Log number of DICOM instances received
                if (affectedInstanceUIDs != null)
                {
                    _logger.Information(
                        $"MPPS contains " +
                        $"{affectedInstanceUIDs.Count} referenced DICOM instances.");
                }


                // ---------------------------------------------------------
                // CALL EXTERNAL API
                // ---------------------------------------------------------

                bool apiResult =
                    _mppsApiService
                        .UpdateMppsStatusAsync(
                            workItem.ProcedureStepID,
                            sopInstanceUID,
                            "COMPLETED", accessionNumber)
                        .GetAwaiter()
                        .GetResult();


                if (!apiResult)
                {
                    _logger.Error(
                        $"Failed to update MPPS status through API. " +
                        $"ProcedureStepID={workItem.ProcedureStepID}, " +
                        $"MPPS UID={sopInstanceUID}, " +
                        $"Status=COMPLETED");

                    // Keep it in PendingProcedures
                    // so that the request can potentially be retried.
                    return false;
                }


                // ---------------------------------------------------------
                // API update successful
                // ---------------------------------------------------------

                PendingProcedures.Remove(sopInstanceUID);


                _logger.Information(
                    $"MPPS API successfully updated. " +
                    $"ProcedureStepID={workItem.ProcedureStepID}, " +
                    $"Status=COMPLETED");


                return true;
            }
            catch (System.Exception ex)
            {
                _logger.Error(
                    $"Exception while processing MPPS COMPLETED. " +
                    $"SOP Instance UID={sopInstanceUID}. " +
                    $"Error={ex.Message}");

                return false;
            }
        }
    }
}


