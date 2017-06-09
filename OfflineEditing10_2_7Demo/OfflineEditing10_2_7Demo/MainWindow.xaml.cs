using Esri.ArcGISRuntime.ArcGISServices;
using Esri.ArcGISRuntime.Controls;
using Esri.ArcGISRuntime.Data;
using Esri.ArcGISRuntime.Http;
using Esri.ArcGISRuntime.Layers;
using Esri.ArcGISRuntime.Security;
using Esri.ArcGISRuntime.Tasks.Offline;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace OfflineEditing10_2_7Demo
{
    public partial class MainWindow : Window
    {

        //change the name to your feature service
        string uristring = "http://sampleserver6.arcgisonline.com/arcgis/rest/services/Sync/SaveTheBaySync/FeatureServer";

        public MainWindow()
        {
            InitializeComponent();
        }

        private void MyMapView_LayerLoaded(object sender, LayerLoadedEventArgs e)
        {
            if (e.LoadError == null)
                return;

            Debug.WriteLine(string.Format("Error while loading layer : {0} - {1}", e.Layer.ID, e.LoadError.Message));
        }

        // provide a callback to execute when the GeodatabaseSyncTask completes (successfully or with an exception)
        private async void GdbCompleteCallback(Esri.ArcGISRuntime.Tasks.Offline.GeodatabaseStatusInfo statusInfo, Exception ex)
        {
            // if unsuccessful, report the exception and return
            if (ex != null)
            {
                Console.WriteLine("An exception occured: " + ex.Message);
                //this.ReportStatus("An exception occured: " + ex.Message);
                return;
            }

            // if successful, read the generated geodatabase from the server
            var client = new ArcGISHttpClient();
            var gdbStream = client.GetOrPostAsync(statusInfo.ResultUri, null);

            var geodatabasePath = System.IO.Path.Combine(@"C:\Temp\10_2_7Demo", "WildlifeLocal.geodatabase");

            // create a local path for the geodatabase, if it doesn't already exist
            if (!System.IO.Directory.Exists(@"C:\Temp\10_2_7Demo"))
            {
                System.IO.Directory.CreateDirectory(@"C:\Temp\10_2_7Demo");
            }

            // write geodatabase to local location
            await Task.Factory.StartNew(async delegate
            {
                using (var stream = System.IO.File.Create(geodatabasePath))
                {
                    await gdbStream.Result.Content.CopyToAsync(stream);
                }
                MessageBox.Show("Offline database created at " + geodatabasePath);
            });
        }

        // store a private variable to manage cancellation of the task
        private CancellationTokenSource _syncCancellationTokenSource;

        private async void Download_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // cancel if an earlier call was made
                if (_syncCancellationTokenSource != null)
                {
                    _syncCancellationTokenSource.Cancel();
                }

                // get a cancellation token
                _syncCancellationTokenSource = new CancellationTokenSource();
                var cancelToken = _syncCancellationTokenSource.Token;

                // create a new GeodatabaseSyncTask with the uri of the feature server to pull from
                var uri = new Uri("http://sampleserver6.arcgisonline.com/arcgis/rest/services/Sync/SaveTheBaySync/FeatureServer");
                var gdbTask = new GeodatabaseSyncTask(uri);

                // create parameters for the task: layers and extent to include, out spatial reference, and sync model
                var layers = new List<int>(new int[3] { 0, 1, 2 });
                var extent = MyMapView.Extent;
                var gdbParams = new GenerateGeodatabaseParameters(layers, extent)
                {
                    OutSpatialReference = MyMapView.SpatialReference,
                    SyncModel = SyncModel.PerLayer,
                    ReturnAttachments = true
                };

                // Create a System.Progress<T> object to report status as the task executes
                var progress = new Progress<GeodatabaseStatusInfo>();
                progress.ProgressChanged += (s, info) =>
                {
                    //ShowStatus(info.Status);
                    Console.WriteLine(info.Status);
                };

                // call GenerateGeodatabaseAsync, pass in the parameters and the callback to execute when it's complete
                var gdbResult = await gdbTask.GenerateGeodatabaseAsync(gdbParams, GdbCompleteCallback, new TimeSpan(0, 1, 0), progress, cancelToken);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Unable to create offline database: " + ex.Message);
            }
        }

        private void syncCompleteCallback(GeodatabaseStatusInfo statusInfo, Exception ex)
        {
            // reset the cancellation token source
            _syncCancellationTokenSource = null;

            // if unsuccessful, report the exception and return
            if (ex != null)
            {
                //this.ReportStatus("An exception occured: " + ex.Message);
                Console.WriteLine("An exception occured: " + ex.Message);
                return;
            }

            // if successful, notify the user
            //this.ReportStatus("Synchronization of '" + statusInfo.GeodatabaseName + "' is complete.");
            Console.WriteLine("Synchronization of '" + statusInfo.GeodatabaseName + "' is complete.");

            // optionally, do something with the result
            var resultUri = statusInfo.ResultUri;
            // ...
        }

        private async void Sync_Click(object sender, RoutedEventArgs e)
        {
            var tcs = new TaskCompletionSource<GeodatabaseStatusInfo>();
            Action<GeodatabaseStatusInfo, Exception> completionAction = (info, ex) =>
            {
                if (ex != null)
                    tcs.SetException(ex);
                tcs.SetResult(info);
            };

            Geodatabase gdb = null;
            var syncTask = new GeodatabaseSyncTask(new Uri(uristring));
            try
            {
                gdb = await Esri.ArcGISRuntime.Data.Geodatabase.OpenAsync(@"C:\Temp\10_2_7Demo\WildlifeLocal.geodatabase");
                var table = gdb.FeatureTables.FirstOrDefault();

                var lyr = new FeatureLayer
                {
                    ID = table.Name,
                    DisplayName = table.Name,
                    FeatureTable = table
                };
                
                MyMapView.Map.Layers.Add(lyr);

                Console.WriteLine(table.Name);
            }
            catch (Exception et)
            {
                Console.WriteLine(et.Message);
            }

            var taskParameters = new SyncGeodatabaseParameters()
            {
                RollbackOnFailure = false,
                SyncDirection = Esri.ArcGISRuntime.Tasks.Offline.SyncDirection.Bidirectional
            };

            // cancel if an earlier call was made and hasn't completed
            if (_syncCancellationTokenSource != null)
            {
                _syncCancellationTokenSource.Cancel();
            }

            // create a new cancellation token
            _syncCancellationTokenSource = new CancellationTokenSource();
            var cancelToken = _syncCancellationTokenSource.Token;

            // create a new Progress object to report updates to the sync status
            var progress = new Progress<GeodatabaseStatusInfo>();
            progress.ProgressChanged += (sender2, s) =>
            {
                Console.WriteLine(s.Status);
            };

            try
            {
                // call SyncGeodatabaseAsync and pass in: sync params, local geodatabase, completion callback, update interval, progress, and cancellation token
                var gdbResult = await syncTask.SyncGeodatabaseAsync(
                    taskParameters,
                    gdb,
                    this.syncCompleteCallback,
                    null,
                    new TimeSpan(0, 0, 2),
                    progress,
                    cancelToken);
            }
            catch (ArcGISWebException a)
            {
                Console.WriteLine(a.Code);
                Console.WriteLine(a.StackTrace);
            }
        }
    }
}
