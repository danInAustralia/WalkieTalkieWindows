using Helpers;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Devices.Enumeration;
using Windows.Devices.WiFiDirect;
using Windows.Devices.WiFiDirect.Services;
using Windows.Foundation;
using Windows.Foundation.Collections;
using Windows.UI.Core;
using Windows.UI.Popups;
using Windows.UI.Xaml;
using Windows.UI.Xaml.Controls;
using Windows.UI.Xaml.Controls.Primitives;
using Windows.UI.Xaml.Data;
using Windows.UI.Xaml.Input;
using Windows.UI.Xaml.Media;
using Windows.UI.Xaml.Navigation;

// The Blank Page item template is documented at http://go.microsoft.com/fwlink/?LinkId=402352&clcid=0x409

namespace WalkieTalkieWindows
{
    /// <summary>
    /// presents peers, allows selection to instigate connection, and listens for incoming connections 
    /// </summary>
    public sealed partial class MainPage : Page
    {
        DeviceWatcher _deviceWatcher;
        WiFiDirectAdvertisementPublisher _publisher = new WiFiDirectAdvertisementPublisher();
        bool _fWatcherStarted = false;

        
        WiFiDirectConnectionListener _listener;

        public ObservableCollection<DetectedWifiDirectDevice> DiscoveredDevices { get; } = new ObservableCollection<DetectedWifiDirectDevice>();
        //public ObservableCollection<ConnectedDevice> ConnectedDevices { get; } = new ObservableCollection<ConnectedDevice>();
        public MainPage()
        {
            this.InitializeComponent();
            SetupWifiDirect();
        }

        private async void SetupWifiDirect()
        {
            ListenForIncomingConnections();
            if (_fWatcherStarted == false)
            {
                _publisher.Advertisement.ListenStateDiscoverability = WiFiDirectAdvertisementListenStateDiscoverability.Normal;
                //make this device known
                _publisher.Start();

                if (_publisher.Status != WiFiDirectAdvertisementPublisherStatus.Started)
                {
                    NotifyUser("Failed to start advertisement.", NotifyType.ErrorMessage);
                    return;
                }

                //detect other devices
                DiscoveredDevices.Clear();
                NotifyUser("Finding Devices...", NotifyType.StatusMessage);

                String deviceSelector = WiFiDirectDevice.GetDeviceSelector(WiFiDirectDeviceSelectorType.AssociationEndpoint);//WiFiDirectDevice.GetDeviceSelector(
                    //Utils.GetSelectedItemTag<WiFiDirectDeviceSelectorType>(cmbDeviceSelector));

                _deviceWatcher = DeviceInformation.CreateWatcher(deviceSelector, new string[] { "System.Devices.WiFiDirect.InformationElements" });

                _deviceWatcher.Added += OnDeviceAdded;
                _deviceWatcher.Removed += OnDeviceRemoved;
                _deviceWatcher.Updated += OnDeviceUpdated;
                _deviceWatcher.EnumerationCompleted += OnEnumerationCompleted;
                _deviceWatcher.Stopped += OnStopped;

                _deviceWatcher.Start();

                //btnWatcher.Content = "Stop Watcher";
                _fWatcherStarted = true;
            }
            else
            {
                _publisher.Stop();

                //btnWatcher.Content = "Start Watcher";
                _fWatcherStarted = false;

                _deviceWatcher.Added -= OnDeviceAdded;
                _deviceWatcher.Removed -= OnDeviceRemoved;
                _deviceWatcher.Updated -= OnDeviceUpdated;
                _deviceWatcher.EnumerationCompleted -= OnEnumerationCompleted;
                _deviceWatcher.Stopped -= OnStopped;

                _deviceWatcher.Stop();

                NotifyUser("Device watcher stopped.", NotifyType.StatusMessage);
            }


            string ServiceSelector = WiFiDirectService.GetSelector("WalkieTalkie");

            // Get all WiFiDirect services that are advertising and in range
            //DeviceInformationCollection devInfoCollection = await DeviceInformation.FindAllAsync(ServiceSelector);

            //// Get a Service Seeker object
            //WiFiDirectService Service = await WiFiDirectService.FromIdAsync(devInfoCollection[0].Id);

            //// Connect to the Advertiser
            //WiFiDirectServiceSession Session = await Service.ConnectAsync();

            //// Get the local and remote IP addresses
            //var EndpointPairs = Session.GetConnectionEndpointPairs();
        }

        private void ListenForIncomingConnections()
        {
            _listener = new WiFiDirectConnectionListener();
            _listener.ConnectionRequested += OnConnectionRequested;
        }

        private async void OnConnectionRequested(WiFiDirectConnectionListener sender, WiFiDirectConnectionRequestedEventArgs connectionEventArgs)
        {
            WiFiDirectConnectionRequest connectionRequest = connectionEventArgs.GetConnectionRequest();
            bool success = await Dispatcher.RunTaskAsync(async () =>
            {
                return await HandleConnectionRequestAsync(connectionRequest);
            });

            if (!success)
            {
                // Decline the connection request
                //rootPage.NotifyUserFromBackground($"Connection request from {connectionRequest.DeviceInformation.Name} was declined", NotifyType.ErrorMessage);
                connectionRequest.Dispose();
            }
        }

        private async Task<bool> IsAepPairedAsync(string deviceId)
        {
            List<string> additionalProperties = new List<string>();
            additionalProperties.Add("System.Devices.Aep.DeviceAddress");
            String deviceSelector = $"System.Devices.Aep.AepId:=\"{deviceId}\"";
            DeviceInformation devInfo = null;

            try
            {
                devInfo = await DeviceInformation.CreateFromIdAsync(deviceId, additionalProperties);
            }
            catch (Exception ex)
            {
                //rootPage.NotifyUser("DeviceInformation.CreateFromIdAsync threw an exception: " + ex.Message, NotifyType.ErrorMessage);
            }

            if (devInfo == null)
            {
                //rootPage.NotifyUser("Device Information is null", NotifyType.ErrorMessage);
                return false;
            }

            deviceSelector = $"System.Devices.Aep.DeviceAddress:=\"{devInfo.Properties["System.Devices.Aep.DeviceAddress"]}\"";
            DeviceInformationCollection pairedDeviceCollection = await DeviceInformation.FindAllAsync(deviceSelector, null, DeviceInformationKind.Device);
            return pairedDeviceCollection.Count > 0;
        }

        /// <summary>
        /// triggered when another device requests wifi direct connection
        /// </summary>
        /// <param name="connectionRequest"></param>
        /// <returns></returns>
        private async Task<bool> HandleConnectionRequestAsync(WiFiDirectConnectionRequest connectionRequest)
        {
            string deviceName = connectionRequest.DeviceInformation.Name;

            bool isPaired = (connectionRequest.DeviceInformation.Pairing?.IsPaired == true) ||
                            (await IsAepPairedAsync(connectionRequest.DeviceInformation.Id));

            // Show the prompt only in case of WiFiDirect reconnection or Legacy client connection.
            //ask user to whether or not to accept incoming connection request
            if (isPaired || _publisher.Advertisement.LegacySettings.IsEnabled)
            {
                var messageDialog = new MessageDialog($"Connection request received from a {deviceName}", "Connection Request");

                // Add two commands, distinguished by their tag.
                // The default command is "Decline", and if the user cancels, we treat it as "Decline".
                messageDialog.Commands.Add(new UICommand("Accept", null, true));
                messageDialog.Commands.Add(new UICommand("Decline", null, null));
                messageDialog.DefaultCommandIndex = 1;
                messageDialog.CancelCommandIndex = 1;

                // Show the message dialog
                var commandChosen = messageDialog.ShowAsync();

                if (commandChosen.Id == null)
                {
                    return false;
                }
            }

            StatusBlock.Text = $"Connecting to {deviceName}...";

            // Pair device if not already paired and not using legacy settings
            if (!isPaired && !_publisher.Advertisement.LegacySettings.IsEnabled)
            {
                if (!await RequestPairDeviceAsync(connectionRequest.DeviceInformation.Pairing))
                {
                    return false;
                }
            }

            WiFiDirectDevice wfdDevice = null;
            try
            {
                // IMPORTANT: FromIdAsync needs to be called from the UI thread
                wfdDevice = await WiFiDirectDevice.FromIdAsync(connectionRequest.DeviceInformation.Id);
            }
            catch (Exception ex)
            {
                StatusBlock.Text = $"Exception in FromIdAsync: {ex}";
                return false;
            }

            // Register for the ConnectionStatusChanged event handler
            wfdDevice.ConnectionStatusChanged += OnConnectionStatusChanged;

            var EndpointPairs = wfdDevice.GetConnectionEndpointPairs();

            //listenerSocket.ConnectionReceived += OnSocketConnectionReceived;
            //try
            //{
            //    await listenerSocket.BindEndpointAsync(EndpointPairs[0].LocalHostName, Globals.strServerPort);
            //}
            //catch (Exception ex)
            //{
            //    rootPage.NotifyUser($"Connect operation threw an exception: {ex.Message}", NotifyType.ErrorMessage);
            //    return false;
            //}

            //rootPage.NotifyUser($"Devices connected on L2, listening on IP Address: {EndpointPairs[0].LocalHostName}" +
            //                    $" Port: {Globals.strServerPort}", NotifyType.StatusMessage);
            return true;
        }

        public async Task<bool> RequestPairDeviceAsync(DeviceInformationPairing pairing)
        {
            WiFiDirectConnectionParameters connectionParams = new WiFiDirectConnectionParameters();

            short? groupOwnerIntent = 1;
            if (groupOwnerIntent.HasValue)
            {
                connectionParams.GroupOwnerIntent = groupOwnerIntent.Value;
            }

            DevicePairingKinds devicePairingKinds = DevicePairingKinds.None;
            //if (_supportedConfigMethods.Count > 0)
            //{
            //    // If specific configuration methods were added, then use them.
            //    foreach (var configMethod in _supportedConfigMethods)
            //    {
            //        connectionParams.PreferenceOrderedConfigurationMethods.Add(configMethod);
            //        devicePairingKinds |= WiFiDirectConnectionParameters.GetDevicePairingKinds(configMethod);
            //    }
            //}
            //else
            {
                // If specific configuration methods were not added, then we'll use these pairing kinds.
                devicePairingKinds = DevicePairingKinds.ConfirmOnly;// | DevicePairingKinds.DisplayPin | DevicePairingKinds.ProvidePin;
            }

            connectionParams.PreferredPairingProcedure = WiFiDirectPairingProcedure.GroupOwnerNegotiation;
            DeviceInformationCustomPairing customPairing = pairing.Custom;
            customPairing.PairingRequested += OnPairingRequested;

            DevicePairingResult result = await customPairing.PairAsync(devicePairingKinds, DevicePairingProtectionLevel.Default, connectionParams);
            if (result.Status != DevicePairingResultStatus.Paired)
            {
                StatusBlock.Text = $"PairAsync failed, Status: {result.Status}";
                return false;
            }
            return true;
        }

        private void OnPairingRequested(DeviceInformationCustomPairing sender, DevicePairingRequestedEventArgs args)
        {
            HandlePairing(Dispatcher, args);
        }

        public static async void HandlePairing(CoreDispatcher dispatcher, DevicePairingRequestedEventArgs args)
        {
            using (Deferral deferral = args.GetDeferral())
            {
                switch (args.PairingKind)
                {
                    case DevicePairingKinds.DisplayPin:
                        //await ShowPinToUserAsync(dispatcher, args.Pin);
                        args.Accept();
                        break;

                    case DevicePairingKinds.ConfirmOnly:
                        args.Accept();
                        break;

                    case DevicePairingKinds.ProvidePin:
                        //args.Accept(await GetPinFromUserAsync(dispatcher));
                        break;
                }
            }
        }

        private void OnConnectionStatusChanged(WiFiDirectDevice sender, object arg)
        {
            StatusBlock.Text = $"Connection status changed: {sender.ConnectionStatus}";

            if (sender.ConnectionStatus == WiFiDirectConnectionStatus.Disconnected)
            {
                // TODO: Should we remove this connection from the list?
                // (Yes, probably.)
            }
        }

        //public async Task<bool> RequestPairDeviceAsync(DeviceInformationPairing pairing)
        //{
        //    WiFiDirectConnectionParameters connectionParams = new WiFiDirectConnectionParameters();

        //    short? groupOwnerIntent = 7;
        //    if (groupOwnerIntent.HasValue)
        //    {
        //        connectionParams.GroupOwnerIntent = groupOwnerIntent.Value;
        //    }

        //    DevicePairingKinds devicePairingKinds = DevicePairingKinds.None;
        //    if (_supportedConfigMethods.Count > 0)
        //    {
        //        // If specific configuration methods were added, then use them.
        //        foreach (var configMethod in _supportedConfigMethods)
        //        {
        //            connectionParams.PreferenceOrderedConfigurationMethods.Add(configMethod);
        //            devicePairingKinds |= WiFiDirectConnectionParameters.GetDevicePairingKinds(configMethod);
        //        }
        //    }
        //    else
        //    {
        //        // If specific configuration methods were not added, then we'll use these pairing kinds.
        //        devicePairingKinds = DevicePairingKinds.ConfirmOnly | DevicePairingKinds.DisplayPin | DevicePairingKinds.ProvidePin;
        //    }

        //    connectionParams.PreferredPairingProcedure = Utils.GetSelectedItemTag<WiFiDirectPairingProcedure>(cmbPreferredPairingProcedure);
        //    DeviceInformationCustomPairing customPairing = pairing.Custom;
        //    customPairing.PairingRequested += OnPairingRequested;

        //    DevicePairingResult result = await customPairing.PairAsync(devicePairingKinds, DevicePairingProtectionLevel.Default, connectionParams);
        //    if (result.Status != DevicePairingResultStatus.Paired)
        //    {
        //        rootPage.NotifyUser($"PairAsync failed, Status: {result.Status}", NotifyType.ErrorMessage);
        //        return false;
        //    }
        //    return true;
        //}

        #region DeviceWatcherEvents
        private async void OnDeviceAdded(DeviceWatcher deviceWatcher, DeviceInformation deviceInfo)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                DiscoveredDevices.Add(new DetectedWifiDirectDevice(deviceInfo));
            });
        }

        private async void OnDeviceRemoved(DeviceWatcher deviceWatcher, DeviceInformationUpdate deviceInfoUpdate)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                foreach (DetectedWifiDirectDevice discoveredDevice in DiscoveredDevices)
                {
                    if (discoveredDevice.DeviceInfo.Id == deviceInfoUpdate.Id)
                    {
                        DiscoveredDevices.Remove(discoveredDevice);
                        break;
                    }
                }
            });
        }

        private async void OnDeviceUpdated(DeviceWatcher deviceWatcher, DeviceInformationUpdate deviceInfoUpdate)
        {
            await Dispatcher.RunAsync(CoreDispatcherPriority.Normal, () =>
            {
                foreach (DetectedWifiDirectDevice discoveredDevice in DiscoveredDevices)
                {
                    if (discoveredDevice.DeviceInfo.Id == deviceInfoUpdate.Id)
                    {
                        discoveredDevice.UpdateDeviceInfo(deviceInfoUpdate);
                        break;
                    }
                }
            });
        }

        private void OnEnumerationCompleted(DeviceWatcher deviceWatcher, object o)
        {
            //rootPage.NotifyUserFromBackground("DeviceWatcher enumeration completed", NotifyType.StatusMessage);
        }

        private void OnStopped(DeviceWatcher deviceWatcher, object o)
        {
            //rootPage.NotifyUserFromBackground("DeviceWatcher stopped", NotifyType.StatusMessage);
        }
        #endregion

        /// <summary>
        /// Used to display messages to the user
        /// </summary>
        /// <param name="strMessage"></param>
        /// <param name="type"></param>
        public void NotifyUser(string strMessage, NotifyType type)
        {
            switch (type)
            {
                case NotifyType.StatusMessage:
                    StatusBorder.Background = new SolidColorBrush(Windows.UI.Colors.Green);
                    break;
                case NotifyType.ErrorMessage:
                    StatusBorder.Background = new SolidColorBrush(Windows.UI.Colors.Red);
                    break;
            }
            StatusBlock.Text = strMessage;

            // Collapse the StatusBlock if it has no text to conserve real estate.
            StatusBorder.Visibility = (StatusBlock.Text != String.Empty) ? Visibility.Visible : Visibility.Collapsed;
            if (StatusBlock.Text != String.Empty)
            {
                StatusBorder.Visibility = Visibility.Visible;
                StatusPanel.Visibility = Visibility.Visible;
            }
            else
            {
                StatusBorder.Visibility = Visibility.Collapsed;
                StatusPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            DeviceInformation deviceInfo = ((DetectedWifiDirectDevice)((ListView)sender).SelectedItem).DeviceInfo as DeviceInformation;
            PairWithSelectedDevice(deviceInfo);
        }

        private async void PairWithSelectedDevice(DeviceInformation selectedDevice)
        {
            //set the group owner intent to indicate that we want the receiver to be the owner
            WiFiDirectConnectionParameters connectionParams = new WiFiDirectConnectionParameters();
            connectionParams.GroupOwnerIntent = 1;

            //connect to the selected device
            try
            {
                var wfdDevice = await WiFiDirectDevice.FromIdAsync(selectedDevice.Id, connectionParams);
            }
            catch(Exception e)
            {
                StatusBlock.Text = "Unable to connect";
            }
        }
    }
    public enum NotifyType
    {
        StatusMessage,
        ErrorMessage
    };
}
