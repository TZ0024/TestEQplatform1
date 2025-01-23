// TODO fill in this information for your driver, then remove this line!
//
// ASCOM Telescope hardware class for TEQ1
//
// Description:	 <To be completed by driver developer>
//
// Implements:	ASCOM Telescope interface version: <To be completed by driver developer>
// Author:		(XXX) Your N. Here <your@email.here>
//

using ASCOM;
using ASCOM.Astrometry;
using ASCOM.Astrometry.AstroUtils;
using ASCOM.Astrometry.NOVAS;
using ASCOM.DeviceInterface;
using ASCOM.LocalServer;
//using ASCOM.LocalServer.Server;
using ASCOM.Utilities;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Configuration;
using System.IO.Ports;
using System.Threading.Tasks;
using System.Windows.Forms;
using TEQ1Helper;

namespace ASCOM.TEQ1.Telescope
{
    //
    // TODO Replace the not implemented exceptions with code to implement the function or throw the appropriate ASCOM exception.
    //

    /// <summary>
    /// ASCOM Telescope hardware class for TEQ1.
    /// </summary>
    [HardwareClass()] // Class attribute flag this as a device hardware class that needs to be disposed by the local server when it exits.
    internal static class TelescopeHardware
    {
        // Constants used for Profile persistence
        internal const string comPortProfileName = "COM Port";
        internal const string comPortDefault = "COM1";
        internal const string traceStateProfileName = "Trace Level";
        internal const string traceStateDefault = "true";

        private static string DriverProgId = ""; // ASCOM DeviceID (COM ProgID) for this driver, the value is set by the driver's class initialiser.
        private static string DriverDescription = ""; // The value is set by the driver's class initialiser.
        internal static string comPort; // COM port name (if required)
        private static bool connectedState; // Local server's connected state
        private static bool connecting; // Completion variable for use with the Connect and Disconnect methods
        private static bool runOnce = false; // Flag to enable "one-off" activities only to run once.
        internal static Util utilities; // ASCOM Utilities object for use as required
        internal static AstroUtils astroUtilities; // ASCOM AstroUtilities object for use as required
        internal static TraceLogger tl; // Local server's trace logger object for diagnostic log with information that you specify

        private static List<Guid> uniqueIds = new List<Guid>(); // List of driver instance unique IDs

        /// <summary>
        /// Initializes a new instance of the device Hardware class.
        /// </summary>
        static TelescopeHardware()
        {
            try
            {
                // Create the hardware trace logger in the static initialiser.
                // All other initialisation should go in the InitialiseHardware method.
                tl = new TraceLogger("", "TEQ1.Hardware");

                // DriverProgId has to be set here because it used by ReadProfile to get the TraceState flag.
                DriverProgId = Telescope.DriverProgId; // Get this device's ProgID so that it can be used to read the Profile configuration values

                // ReadProfile has to go here before anything is written to the log because it loads the TraceLogger enable / disable state.
                ReadProfile(); // Read device configuration from the ASCOM Profile store, including the trace state

                LogMessage("TelescopeHardware", $"Static initialiser completed.");
            }
            catch (Exception ex)
            {
                try { LogMessage("TelescopeHardware", $"Initialisation exception: {ex}"); } catch { }
                MessageBox.Show($"{ex.Message}", "Exception creating ASCOM.TEQ1.Telescope", MessageBoxButtons.OK, MessageBoxIcon.Error);
                throw;
            }
        }

        /// <summary>
        /// Place device initialisation code here
        /// </summary>
        /// <remarks>Called every time a new instance of the driver is created.</remarks>
        internal static void InitialiseHardware()
        {
            // This method will be called every time a new ASCOM client loads your driver
            LogMessage("InitialiseHardware", $"Start.");

            // Make sure that "one off" activities are only undertaken once
            if (runOnce == false)
            {
                LogMessage("InitialiseHardware", $"Starting one-off initialisation.");

                DriverDescription = Telescope.DriverDescription; // Get this device's Chooser description

                LogMessage("InitialiseHardware", $"ProgID: {DriverProgId}, Description: {DriverDescription}");

                connectedState = false; // Initialise connected to false
                utilities = new Util(); //Initialise ASCOM Utilities object
                astroUtilities = new AstroUtils(); // Initialise ASCOM Astronomy Utilities object

                LogMessage("InitialiseHardware", "Completed basic initialisation");

                // Add your own "one off" device initialisation here e.g. validating existence of hardware and setting up communications

                LogMessage("InitialiseHardware", $"One-off initialisation complete.");
                runOnce = true; // Set the flag to ensure that this code is not run again
            }
        }

        // PUBLIC COM INTERFACE ITelescopeV4 IMPLEMENTATION

        #region Common properties and methods.

        /// <summary>
        /// Displays the Setup Dialogue form.
        /// If the user clicks the OK button to dismiss the form, then
        /// the new settings are saved, otherwise the old values are reloaded.
        /// THIS IS THE ONLY PLACE WHERE SHOWING USER INTERFACE IS ALLOWED!
        /// </summary>
        public static void SetupDialog()
        {
            // Don't permit the setup dialogue if already connected
            if (IsConnected)
                MessageBox.Show("Already connected, just press OK");

            using (SetupDialogForm F = new SetupDialogForm(tl))
            {
                var result = F.ShowDialog();
                if (result == DialogResult.OK)
                {
                    WriteProfile(); // Persist device configuration values to the ASCOM Profile store
                }
            }
        }

        /// <summary>Returns the list of custom action names supported by this driver.</summary>
        /// <value>An ArrayList of strings (SafeArray collection) containing the names of supported actions.</value>
        public static ArrayList SupportedActions
        {
            get
            {
                LogMessage("SupportedActions Get", "Returning empty ArrayList");
                return new ArrayList();
            }
        }

        /// <summary>Invokes the specified device-specific custom action.</summary>
        /// <param name="ActionName">A well known name agreed by interested parties that represents the action to be carried out.</param>
        /// <param name="ActionParameters">List of required parameters or an <see cref="String.Empty">Empty String</see> if none are required.</param>
        /// <returns>A string response. The meaning of returned strings is set by the driver author.
        /// <para>Suppose filter wheels start to appear with automatic wheel changers; new actions could be <c>QueryWheels</c> and <c>SelectWheel</c>. The former returning a formatted list
        /// of wheel names and the second taking a wheel name and making the change, returning appropriate values to indicate success or failure.</para>
        /// </returns>
        public static string Action(string actionName, string actionParameters)
        {
            LogMessage("Action", $"Action {actionName}, parameters {actionParameters} is not implemented");
            throw new ActionNotImplementedException("Action " + actionName + " is not implemented by this driver");
        }

        /// <summary>
        /// Transmits an arbitrary string to the device and does not wait for a response.
        /// Optionally, protocol framing characters may be added to the string before transmission.
        /// </summary>
        /// <param name="Command">The literal command string to be transmitted.</param>
        /// <param name="Raw">
        /// if set to <c>true</c> the string is transmitted 'as-is'.
        /// If set to <c>false</c> then protocol framing characters may be added prior to transmission.
        /// </param>
        public static void CommandBlind(string command, bool raw)
        {
            CheckConnected("CommandBlind");
            // TODO The optional CommandBlind method should either be implemented OR throw a MethodNotImplementedException
            // If implemented, CommandBlind must send the supplied command to the mount and return immediately without waiting for a response

            throw new MethodNotImplementedException($"CommandBlind - Command:{command}, Raw: {raw}.");
        }

        /// <summary>
        /// Transmits an arbitrary string to the device and waits for a boolean response.
        /// Optionally, protocol framing characters may be added to the string before transmission.
        /// </summary>
        /// <param name="Command">The literal command string to be transmitted.</param>
        /// <param name="Raw">
        /// if set to <c>true</c> the string is transmitted 'as-is'.
        /// If set to <c>false</c> then protocol framing characters may be added prior to transmission.
        /// </param>
        /// <returns>
        /// Returns the interpreted boolean response received from the device.
        /// </returns>
        public static bool CommandBool(string command, bool raw)
        {
            CheckConnected("CommandBool");
            // TODO The optional CommandBool method should either be implemented OR throw a MethodNotImplementedException
            // If implemented, CommandBool must send the supplied command to the mount, wait for a response and parse this to return a True or False value

            throw new MethodNotImplementedException($"CommandBool - Command:{command}, Raw: {raw}.");
        }

        /// <summary>
        /// Transmits an arbitrary string to the device and waits for a string response.
        /// Optionally, protocol framing characters may be added to the string before transmission.
        /// </summary>
        /// <param name="Command">The literal command string to be transmitted.</param>
        /// <param name="Raw">
        /// if set to <c>true</c> the string is transmitted 'as-is'.
        /// If set to <c>false</c> then protocol framing characters may be added prior to transmission.
        /// </param>
        /// <returns>
        /// Returns the string response received from the device.
        /// </returns>
        public static string CommandString(string command, bool raw)
        {
            CheckConnected("CommandString");
            // TODO The optional CommandString method should either be implemented OR throw a MethodNotImplementedException
            // If implemented, CommandString must send the supplied command to the mount and wait for a response before returning this to the client

            throw new MethodNotImplementedException($"CommandString - Command:{command}, Raw: {raw}.");
        }

        /// <summary>
        /// Deterministically release both managed and unmanaged resources that are used by this class.
        /// </summary>
        /// <remarks>
        /// TODO: Release any managed or unmanaged resources that are used in this class.
        /// 
        /// Do not call this method from the Dispose method in your driver class.
        ///
        /// This is because this hardware class is decorated with the <see cref="HardwareClassAttribute"/> attribute and this Dispose() method will be called 
        /// automatically by the  local server executable when it is irretrievably shutting down. This gives you the opportunity to release managed and unmanaged 
        /// resources in a timely fashion and avoid any time delay between local server close down and garbage collection by the .NET runtime.
        ///
        /// For the same reason, do not call the SharedResources.Dispose() method from this method. Any resources used in the static shared resources class
        /// itself should be released in the SharedResources.Dispose() method as usual. The SharedResources.Dispose() method will be called automatically 
        /// by the local server just before it shuts down.
        /// 
        /// </remarks>
        public static void Dispose()
        {
            try { LogMessage("Dispose", $"Disposing of assets and closing down."); } catch { }

            try
            {
                // Clean up the trace logger and utility objects
                tl.Enabled = false;
                tl.Dispose();
                tl = null;
            }
            catch { }

            try
            {
                utilities.Dispose();
                utilities = null;
            }
            catch { }

            try
            {
                astroUtilities.Dispose();
                astroUtilities = null;
            }
            catch { }
        }

        /// <summary>
        /// Connect to the hardware if not already connected
        /// </summary>
        /// <param name="uniqueId">Unique ID identifying the calling driver instance.</param>
        /// <remarks>
        /// The unique ID is stored to record that the driver instance is connected and to ensure that multiple calls from the same driver are ignored.
        /// If this is the first driver instance to connect, the physical hardware link to the device is established
        /// </remarks>
        public static void Connect(Guid uniqueId)
        {
            LogMessage("Connect", $"Device instance unique ID: {uniqueId}");

            // Check whether this driver instance has already connected
            if (uniqueIds.Contains(uniqueId)) // Instance already connected
            {
                // Ignore the request, the unique ID is already in the list
                LogMessage("Connect", $"Ignoring request to connect because the device is already connected.");
                return;
            }

            // Set the connection in progress flag
            connecting = true;

            // Driver instance not yet connected, so start a task to connect to the device hardware and return while the task runs in the background
            // Discard the returned task value because this a "fire and forget" task
            LogMessage("Connect", $"Starting Connect task...");
            _ = Task.Run(() =>
            {
                try
                {
                    // Set the Connected state to true, waiting until it completes
                    LogMessage("ConnectTask", $"Setting connection state to true");
                    SetConnected(uniqueId, true);
                    LogMessage("ConnectTask", $"Connected set true");
                }
                catch (Exception ex)
                {
                    LogMessage("ConnectTask", $"Exception - {ex.Message}\r\n{ex}");
                    throw;
                }
                finally
                {
                    connecting = false;
                    LogMessage("ConnectTask", $"Connecting set false");
                }
            });
            LogMessage("Connect", $"Connect task started OK");
        }

        /// <summary>
        /// Disconnect from the device asynchronously using Connecting as the completion variable
        /// </summary>
        /// <param name="uniqueId">Unique ID identifying the calling driver instance.</param>
        /// <remarks>
        /// The list of connected driver instance IDs is queried to determine whether this driver instance is connected and, if so, it is removed from the connection list. 
        /// The unique ID ensures that multiple calls from the same driver are ignored.
        /// If this is the last connected driver instance, the physical link to the device hardware is disconnected.
        /// </remarks>
        public static void Disconnect(Guid uniqueId)
        {
            LogMessage("Disconnect", $"Device instance unique ID: {uniqueId}");

            // Check whether this driver instance has already disconnected
            if (!uniqueIds.Contains(uniqueId)) // Instance already disconnected
            {
                // Ignore the request, the unique ID is already removed from the list
                LogMessage("Disconnect", $"Ignoring request to disconnect because the device is already disconnected.");
                return;
            }

            // Set the Disconnect in progress flag
            connecting = true;

            // Start a task to disconnect from the device hardware and return while the task runs in the background
            // Discard the returned task value because this a "fire and forget" task
            LogMessage("Disconnect", $"Starting Disconnect task...");
            _ = Task.Run(() =>
            {
                try
                {
                    // Set the Connected state to false, waiting until it completes
                    LogMessage("DisconnectTask", $"Setting connection state to false");
                    SetConnected(uniqueId, false);
                    LogMessage("DisconnectTask", $"Connected set false");
                }
                catch (Exception ex)
                {
                    LogMessage("DisconnectTask", $"Exception - {ex.Message}\r\n{ex}");
                    throw;
                }
                finally
                {
                    connecting = false;
                    LogMessage("DisconnectTask", $"Connecting set false");
                }
            });
            LogMessage("Disconnect", $"Disconnect task started OK");
        }

        /// <summary>
        /// Completion variable for the asynchronous Connect() and Disconnect()  methods
        /// </summary>
        public static bool Connecting
        {
            get
            {
                return connecting;
            }
        }

        /// <summary>
        /// Synchronously connect to or disconnect from the hardware
        /// </summary>
        /// <param name="uniqueId">Driver's unique ID</param>
        /// <param name="newState">New state: Connected or Disconnected</param>
        public static void SetConnected(Guid uniqueId, bool newState)
        {
            // Check whether we are connecting or disconnecting
            if (newState) // We are connecting
            {
                // Check whether this driver instance has already connected
                if (uniqueIds.Contains(uniqueId)) // Instance already connected
                {
                    // Ignore the request, the unique ID is already in the list
                    LogMessage("SetConnected", $"Ignoring request to connect because the device is already connected.");
                }
                else // Instance not already connected, so connect it
                {
                    // Check whether this is the first connection to the hardware
                    if (uniqueIds.Count == 0) // This is the first connection to the hardware so initiate the hardware connection
                    {
                        SharedResources.SharedSerial.PortName = comPort;
                        SharedResources.SharedSerial.Speed = SerialSpeed.ps57600;
                        SharedResources.Connected = true;
                        
                        
                        LogMessage("SetConnected", $"Connecting to hardware.");
                    }
                    else // Other device instances are connected so the hardware is already connected
                    {
                        // Since the hardware is already connected no action is required
                        LogMessage("SetConnected", $"Hardware already connected.");
                    }
                    connectedState = true;
                    // The hardware either "already was" or "is now" connected, so add the driver unique ID to the connected list
                    uniqueIds.Add(uniqueId);
                    LogMessage("SetConnected", $"Unique id {uniqueId} added to the connection list.");
                }
                
            }
            else // We are disconnecting
            {
                // Check whether this driver instance has already disconnected
                if (!uniqueIds.Contains(uniqueId)) // Instance not connected so ignore request
                {
                    // Ignore the request, the unique ID is not in the list
                    LogMessage("SetConnected", $"Ignoring request to disconnect because the device is already disconnected.");
                }
                else // Instance currently connected so disconnect it
                {
                    // Remove the driver unique ID to the connected list
                    uniqueIds.Remove(uniqueId);
                    LogMessage("SetConnected", $"Unique id {uniqueId} removed from the connection list.");

                    // Check whether there are now any connected driver instances 
                    if (uniqueIds.Count == 0) // There are no connected driver instances so disconnect from the hardware
                    {
                        //
                        SharedResources.Connected = false;
                        connectedState = false;
                        //
                    }
                    else // Other device instances are connected so do not disconnect the hardware
                    {
                        // No action is required
                        LogMessage("SetConnected", $"Hardware already connected.");
                    }
                }
            }

            // Log the current connected state
            LogMessage("SetConnected", $"Currently connected driver ids:");
            foreach (Guid id in uniqueIds)
            {
                LogMessage("SetConnected", $" ID {id} is connected");
            }
        }

        /// <summary>
        /// Returns a description of the device, such as manufacturer and model number. Any ASCII characters may be used.
        /// </summary>
        /// <value>The description.</value>
        public static string Description
        {
            // TODO customise this device description if required
            get
            {
                LogMessage("Description Get", DriverDescription);
                return DriverDescription;
            }
        }

        /// <summary>
        /// Descriptive and version information about this ASCOM driver.
        /// </summary>
        public static string DriverInfo
        {
            get
            {
                Version version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                // TODO customise this driver description if required
                string driverInfo = $"Information about the driver itself. Version: {version.Major}.{version.Minor}";
                LogMessage("DriverInfo Get", driverInfo);
                return driverInfo;
            }
        }

        /// <summary>
        /// A string containing only the major and minor version of the driver formatted as 'm.n'.
        /// </summary>
        public static string DriverVersion
        {
            get
            {
                Version version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                string driverVersion = $"{version.Major}.{version.Minor}";
                LogMessage("DriverVersion Get", driverVersion);
                return driverVersion;
            }
        }

        /// <summary>
        /// The interface version number that this device supports.
        /// </summary>
        public static short InterfaceVersion
        {
            // set by the driver wizard
            get
            {
                LogMessage("InterfaceVersion Get", "4");
                return Convert.ToInt16("4");
            }
        }

        /// <summary>
        /// The short name of the driver, for display purposes
        /// </summary>
        public static string Name
        {
            // TODO customise this device name as required
            get
            {
                string name = "ASCOM.TestEQP1.driver";
                LogMessage("Name Get", name);
                return name;
            }
        }

        #endregion

        #region ITelescope Implementation

        /// <summary>
        /// Stops a slew in progress.
        /// </summary>
        internal static void AbortSlew()
        {
            try
            {
                bool ret;
                string s;
                s = SharedResources.SendMessage("A#");
                //s = s.Substring(0, s.Length - 1);
                s = HelperClass1.GetUntil(s, '#');
                ret = s.Equals("TRUE", StringComparison.Ordinal);
                tl.LogMessage("AbortSlew", "recieved - " + ret.ToString());
            }
            catch (Exception ex)
            {
                LogMessage("AbortSlew task", $"Exception - {ex.Message}\r\n{ex}");
                throw;
            }
        }

        /// <summary>
        /// The alignment mode of the mount (Alt/Az, Polar, German Polar).
        /// </summary>
        internal static AlignmentModes AlignmentMode
        {
            get
            {
                return AlignmentModes.algPolar;
            }
        }

        /// <summary>
        /// The Altitude above the local horizon of the telescope's current position (degrees, positive up)
        /// </summary>
        internal static double Altitude
        {
            get
            {
                LogMessage("Altitude", "Not implemented");
                throw new PropertyNotImplementedException("Altitude", false);
            }
        }

        /// <summary>
        /// The area of the telescope's aperture, taking into account any obstructions (square meters)
        /// </summary>
        internal static double ApertureArea
        {
            get
            {
                LogMessage("ApertureArea Get", "Not implemented");
                throw new PropertyNotImplementedException("ApertureArea", false);
            }
        }

        /// <summary>
        /// The telescope's effective aperture diameter (meters)
        /// </summary>
        internal static double ApertureDiameter
        {
            get
            {
                LogMessage("ApertureDiameter Get", "Not implemented");
                throw new PropertyNotImplementedException("ApertureDiameter", false);
            }
        }

        /// <summary>
        /// True if the telescope is stopped in the Home position. Set only following a <see cref="FindHome"></see> operation,
        /// and reset with any slew operation. This property must be False if the telescope does not support homing.
        /// </summary>
        internal static bool AtHome
        {
            get
            {
                try
                {
                    bool ret;
                    string s;
                    s = SharedResources.SendMessage("ATHOME#");
                    // s.Replace("#", "");
                    //s = s.Substring(0, s.Length - 1);
                    s = HelperClass1.GetUntil(s, '#');
                    ret = s.Equals("TRUE", StringComparison.Ordinal);
                    LogMessage("AtHome", "Get - " + ret.ToString());
                    return ret;
                }
                catch (Exception ex)
                {
                    LogMessage("AtHome task", $"Exception - {ex.Message}\r\n{ex}");
                    throw;
                }
            }
        }

        /// <summary>
        /// True if the telescope has been put into the parked state by the seee <see cref="Park" /> method. Set False by calling the Unpark() method.
        /// </summary>
        internal static bool AtPark
        {
            get
            {
                LogMessage("AtPark", "Get - " + false.ToString());
                return false;
            }
        }

        /// <summary>
        /// Determine the rates at which the telescope may be moved about the specified axis by the <see cref="MoveAxis" /> method.
        /// </summary>
        /// <param name="Axis">The axis about which rate information is desired (TelescopeAxes value)</param>
        /// <returns>Collection of <see cref="IRate" /> rate objects</returns>
        internal static IAxisRates AxisRates(TelescopeAxes Axis)
        {
            LogMessage("AxisRates", "Get - " + Axis.ToString());
            return new AxisRates(Axis);
        }

        /// <summary>
        /// The azimuth at the local horizon of the telescope's current position (degrees, North-referenced, positive East/clockwise).
        /// </summary>
        internal static double Azimuth
        {
            get
            {
                LogMessage("Azimuth Get", "Not implemented");
                throw new PropertyNotImplementedException("Azimuth", false);
            }
        }

        /// <summary>
        /// True if this telescope is capable of programmed finding its home position (<see cref="FindHome" /> method).
        /// </summary>
        internal static bool CanFindHome
        {
            get
            {
                LogMessage("CanFindHome", "Get - " + true.ToString());
                return true;
            }
        }

        /// <summary>
        /// True if this telescope can move the requested axis
        /// </summary>
        internal static bool CanMoveAxis(TelescopeAxes Axis)
        {
            LogMessage("CanMoveAxis", "Get - " + Axis.ToString());
            switch (Axis)
            {
                case TelescopeAxes.axisPrimary: return false;
                case TelescopeAxes.axisSecondary: return false;
                case TelescopeAxes.axisTertiary: return false;
                default: throw new InvalidValueException("CanMoveAxis", Axis.ToString(), "0 to 2");
            }
        }

        /// <summary>
        /// True if this telescope is capable of programmed parking (<see cref="Park" />method)
        /// </summary>
        internal static bool CanPark
        {
            get
            {
                LogMessage("CanPark", "Get - " + false.ToString());
                return false;
            }
        }

        /// <summary>
        /// True if this telescope is capable of software-pulsed guiding (via the <see cref="PulseGuide" /> method)
        /// </summary>
        internal static bool CanPulseGuide
        {
            get
            {
                LogMessage("CanPulseGuide", "Get - " + true.ToString());
                return true;
            }
        }

        /// <summary>
        /// True if the <see cref="DeclinationRate" /> property can be changed to provide offset tracking in the declination axis.
        /// </summary>
        internal static bool CanSetDeclinationRate
        {
            get
            {
                LogMessage("CanSetDeclinationRate", "Get - " + false.ToString());
                return false;
            }
        }

        /// <summary>
        /// True if the guide rate properties used for <see cref="PulseGuide" /> can ba adjusted.
        /// </summary>
        internal static bool CanSetGuideRates
        {
            get
            {
                LogMessage("CanSetGuideRates", "Get - " + false.ToString());
                return false;
            }
        }

        /// <summary>
        /// True if this telescope is capable of programmed setting of its park position (<see cref="SetPark" /> method)
        /// </summary>
        internal static bool CanSetPark
        {
            get
            {
                LogMessage("CanSetPark", "Get - " + false.ToString());
                return false;
            }
        }

        /// <summary>
        /// True if the <see cref="SideOfPier" /> property can be set, meaning that the mount can be forced to flip.
        /// </summary>
        internal static bool CanSetPierSide
        {
            get
            {
                LogMessage("CanSetPierSide", "Get - " + false.ToString());
                return false;
            }
        }

        /// <summary>
        /// True if the <see cref="RightAscensionRate" /> property can be changed to provide offset tracking in the right ascension axis.
        /// </summary>
        internal static bool CanSetRightAscensionRate
        {
            get
            {
                LogMessage("CanSetRightAscensionRate", "Get - " + false.ToString());
                return false;
            }
        }

        /// <summary>
        /// True if the <see cref="Tracking" /> property can be changed, turning telescope sidereal tracking on and off.
        /// </summary>
        internal static bool CanSetTracking
        {
            get
            {
                LogMessage("CanSetTracking", "Get - " + true.ToString());
                return true;
            }
        }

        /// <summary>
        /// True if this telescope is capable of programmed slewing (synchronous or asynchronous) to equatorial coordinates
        /// </summary>
        internal static bool CanSlew
        {
            get
            {
                LogMessage("CanSlew", "Get - " + false.ToString());
                return false;
            }
        }

        /// <summary>
        /// True if this telescope is capable of programmed slewing (synchronous or asynchronous) to local horizontal coordinates
        /// </summary>
        internal static bool CanSlewAltAz
        {
            get
            {
                LogMessage("CanSlewAltAz", "Get - " + false.ToString());
                return false;
            }
        }

        /// <summary>
        /// True if this telescope is capable of programmed asynchronous slewing to local horizontal coordinates
        /// </summary>
        internal static bool CanSlewAltAzAsync
        {
            get
            {
                LogMessage("CanSlewAltAzAsync", "Get - " + false.ToString());
                return false;
            }
        }

        /// <summary>
        /// True if this telescope is capable of programmed asynchronous slewing to equatorial coordinates.
        /// </summary>
        internal static bool CanSlewAsync
        {
            get
            {
                LogMessage("CanSlewAsync", "Get - " + false.ToString());
                return false;
            }
        }

        /// <summary>
        /// True if this telescope is capable of programmed syncing to equatorial coordinates.
        /// </summary>
        internal static bool CanSync
        {
            get
            {
                LogMessage("CanSync", "Get - " + false.ToString());
                return false;
            }
        }

        /// <summary>
        /// True if this telescope is capable of programmed syncing to local horizontal coordinates
        /// </summary>
        internal static bool CanSyncAltAz
        {
            get
            {
                LogMessage("CanSyncAltAz", "Get - " + false.ToString());
                return false;
            }
        }

        /// <summary>
        /// True if this telescope is capable of programmed unparking (<see cref="Unpark" /> method).
        /// </summary>
        internal static bool CanUnpark
        {
            get
            {
                LogMessage("CanUnpark", "Get - " + false.ToString());
                return false;
            }
        }

        /// <summary>
        /// The declination (degrees) of the telescope's current equatorial coordinates, in the coordinate system given by the <see cref="EquatorialSystem" /> property.
        /// Reading the property will raise an error if the value is unavailable.
        /// </summary>
        internal static double Declination
        {
            get
            {
                double declination = 0.0;
                LogMessage("Declination", "Get - " + utilities.DegreesToDMS(declination, ":", ":"));
                return declination;
            }
        }

        /// <summary>
        /// The declination tracking rate (arcseconds per SI second, default = 0.0)
        /// </summary>
        internal static double DeclinationRate
        {
            get
            {
                double declination = 0.0;
                LogMessage("DeclinationRate", "Get - " + declination.ToString());
                return declination;
            }
            set
            {
                LogMessage("DeclinationRate Set", "Not implemented");
                throw new PropertyNotImplementedException("DeclinationRate", true);
            }
        }

        /// <summary>
        /// Predict side of pier for German equatorial mounts at the provided coordinates
        /// </summary>
        internal static PierSide DestinationSideOfPier(double RightAscension, double Declination)
        {
            LogMessage("DestinationSideOfPier Get", "Not implemented");
            throw new PropertyNotImplementedException("DestinationSideOfPier", false);
        }

        /// <summary>
        /// True if the telescope or driver applies atmospheric refraction to coordinates.
        /// </summary>
        internal static bool DoesRefraction
        {
            get
            {
                LogMessage("DoesRefraction Get", "Not implemented");
                throw new PropertyNotImplementedException("DoesRefraction", false);
            }
            set
            {
                LogMessage("DoesRefraction Set", "Not implemented");
                throw new PropertyNotImplementedException("DoesRefraction", true);
            }
        }

        /// <summary>
        /// Equatorial coordinate system used by this telescope (e.g. Topocentric or J2000).
        /// </summary>
        internal static EquatorialCoordinateType EquatorialSystem
        {
            get
            {
                EquatorialCoordinateType equatorialSystem = EquatorialCoordinateType.equTopocentric;
                LogMessage("DeclinationRate", "Get - " + equatorialSystem.ToString());
                return equatorialSystem;
            }
        }
        
        /// <summary>
        /// Locates the telescope's "home" position (synchronous)
        /// </summary>
        internal static void FindHome()
        {
            try
            {
                string s;
                s = SharedResources.SendMessage("HOME#");
                tl.LogMessage("FindHome task - ", s);
                return;
            }
            catch (Exception ex)
            {
                LogMessage("FindHome task", $"Exception - {ex.Message}\r\n{ex}");
                throw;
            }
            
        }

        /// <summary>
        /// The telescope's focal length, meters
        /// </summary>
        internal static double FocalLength
        {
            get
            {
                LogMessage("FocalLength Get", "Not implemented");
                throw new PropertyNotImplementedException("FocalLength", false);
            }
        }

        /// <summary>
        /// The current Declination movement rate offset for telescope guiding (degrees/sec)
        /// </summary>
        internal static double GuideRateDeclination
        {
            get
            {
                LogMessage("GuideRateDeclination Get", "Not implemented");
                throw new PropertyNotImplementedException("GuideRateDeclination", false);
            }
            set
            {
                LogMessage("GuideRateDeclination Set", "Not implemented");
                throw new PropertyNotImplementedException("GuideRateDeclination", true);
            }
        }

        /// <summary>
        /// The current Right Ascension movement rate offset for telescope guiding (degrees/sec)
        /// </summary>
        internal static double GuideRateRightAscension
        {
            get
            {
                LogMessage("GuideRateRightAscension Get", "Not implemented");
                throw new PropertyNotImplementedException("GuideRateRightAscension", false);
            }
            set
            {
                LogMessage("GuideRateRightAscension Set", "Not implemented");
                throw new PropertyNotImplementedException("GuideRateRightAscension", true);
            }
        }

        /// <summary>
        /// True if a <see cref="PulseGuide" /> command is in progress, False otherwise
        /// </summary>
        internal static bool IsPulseGuiding
        {
            get
            {
                try
                {
                    string s;
                    s = SharedResources.SendMessage("I#");
                    //s = s.Substring(0, s.Length - 1);
                    s = HelperClass1.GetUntil(s, '#');
                    bool ret = s.Equals("TRUE", StringComparison.Ordinal);

                    tl.LogMessage("IsPulseGuiding Get - ", ret.ToString());
                    return ret;
                }
                catch (Exception ex)
                {
                    LogMessage("IsPulseGuiding task", $"Exception - {ex.Message}\r\n{ex}");
                    throw;
                }
            }
        }

        /// <summary>
        /// Move the telescope in one axis at the given rate.
        /// </summary>
        /// <param name="Axis">The physical axis about which movement is desired</param>
        /// <param name="Rate">The rate of motion (deg/sec) about the specified axis</param>
        internal static void MoveAxis(TelescopeAxes Axis, double Rate)
        {
            LogMessage("MoveAxis", "Not implemented");
            throw new MethodNotImplementedException("MoveAxis");
        }


        /// <summary>
        /// Move the telescope to its park position, stop all motion (or restrict to a small safe range), and set <see cref="AtPark" /> to True.
        /// </summary>
        internal static void Park()
        {
            LogMessage("Park", "Not implemented");
            throw new MethodNotImplementedException("Park");
        }

        /// <summary>
        /// Moves the scope in the given direction for the given interval or time at
        /// the rate given by the corresponding guide rate property
        /// </summary>
        /// <param name="Direction">The direction in which the guide-rate motion is to be made</param>
        /// <param name="Duration">The duration of the guide-rate motion (milliseconds)</param>
        internal static void PulseGuide(GuideDirections Direction, int Duration)
        {
            try
            {
                //command format is "E#500#"
                if (Direction == GuideDirections.guideEast) //East
                {
                    string s;
                    s = SharedResources.SendMessage("E#" + Duration.ToString() + "#");
                    //s = s.Substring(0, s.Length - 1);
                    s = HelperClass1.GetUntil(s, '#');
                    bool ret = s.Equals("TRUE", StringComparison.Ordinal);
                    tl.LogMessage("PulseGuide E" + Duration.ToString(), "recieved - " + ret.ToString());
                }
                else if (Direction == GuideDirections.guideWest) //West
                {
                    string s;
                    s = SharedResources.SendMessage("W#" + Duration.ToString() + "#");
                    //s = s.Substring(0, s.Length - 1);
                    s = HelperClass1.GetUntil(s, '#');
                    bool ret = s.Equals("TRUE", StringComparison.Ordinal);
                    tl.LogMessage("PulseGuide W" + Duration.ToString(), "recieved - " + ret.ToString());
                }
            }
            catch (Exception ex)
            {
                LogMessage("PulseGuide task", $"Exception - {ex.Message}\r\n{ex}");
                throw;
            }
            
        }

        /// <summary>
        /// The right ascension (hours) of the telescope's current equatorial coordinates,
        /// in the coordinate system given by the EquatorialSystem property
        /// </summary>
        internal static double RightAscension
        {
            get
            {
                double rightAscension = 0.0;
                LogMessage("RightAscension", "Get - " + utilities.HoursToHMS(rightAscension));
                return rightAscension;
            }
        }

        /// <summary>
        /// The right ascension tracking rate offset from sidereal (seconds per sidereal second, default = 0.0)
        /// </summary>
        internal static double RightAscensionRate
        {
            get
            {
                double rightAscensionRate = 0.0;
                LogMessage("RightAscensionRate", "Get - " + rightAscensionRate.ToString());
                return rightAscensionRate;
            }
            set
            {
                LogMessage("RightAscensionRate Set", "Not implemented");
                throw new PropertyNotImplementedException("RightAscensionRate", true);
            }
        }

        /// <summary>
        /// Sets the telescope's park position to be its current position.
        /// </summary>
        internal static void SetPark()
        {
            LogMessage("SetPark", "Not implemented");
            throw new MethodNotImplementedException("SetPark");
        }

        /// <summary>
        /// Indicates the pointing state of the mount. Read the articles installed with the ASCOM Developer
        /// Components for more detailed information.
        /// </summary>
        internal static PierSide SideOfPier
        {
            get
            {
                LogMessage("SideOfPier Get", "Not implemented");
                throw new PropertyNotImplementedException("SideOfPier", false);
            }
            set
            {
                LogMessage("SideOfPier Set", "Not implemented");
                throw new PropertyNotImplementedException("SideOfPier", true);
            }
        }

        /// <summary>
        /// The local apparent sidereal time from the telescope's internal clock (hours, sidereal)
        /// </summary>
        internal static double SiderealTime
        {
            get
            {
                double siderealTime = 0.0; // Sidereal time return value

                // Use NOVAS 3.1 to calculate the sidereal time
                using (var novas = new NOVAS31())
                {
                    double julianDate = utilities.DateUTCToJulian(DateTime.UtcNow);
                    novas.SiderealTime(julianDate, 0, novas.DeltaT(julianDate), GstType.GreenwichApparentSiderealTime, Method.EquinoxBased, Accuracy.Full, ref siderealTime);
                }

                // Adjust the calculated sidereal time for longitude using the value returned by the SiteLongitude property, allowing for the possibility that this property has not yet been implemented
                try
                {
                    siderealTime += SiteLongitude / 360.0 * 24.0;
                }
                catch (PropertyNotImplementedException) // SiteLongitude hasn't been implemented
                {
                    // No action, just return the calculated sidereal time unadjusted for longitude
                }
                catch (Exception) // Some other exception occurred so return it to the client
                {
                    throw;
                }

                // Reduce sidereal time to the range 0 to 24 hours
                siderealTime = astroUtilities.ConditionRA(siderealTime);

                LogMessage("SiderealTime", "Get - " + siderealTime.ToString());
                return siderealTime;
            }
        }

        /// <summary>
        /// The elevation above mean sea level (meters) of the site at which the telescope is located
        /// </summary>
        internal static double SiteElevation
        {
            get
            {
                LogMessage("SiteElevation Get", "Not implemented");
                throw new PropertyNotImplementedException("SiteElevation", false);
            }
            set
            {
                LogMessage("SiteElevation Set", "Not implemented");
                throw new PropertyNotImplementedException("SiteElevation", true);
            }
        }

        /// <summary>
        /// The geodetic(map) latitude (degrees, positive North, WGS84) of the site at which the telescope is located.
        /// </summary>
        internal static double SiteLatitude
        {
            get
            {
                LogMessage("SiteLatitude Get", "Not implemented");
                throw new PropertyNotImplementedException("SiteLatitude", false);
            }
            set
            {
                LogMessage("SiteLatitude Set", "Not implemented");
                throw new PropertyNotImplementedException("SiteLatitude", true);
            }
        }

        /// <summary>
        /// The longitude (degrees, positive East, WGS84) of the site at which the telescope is located.
        /// </summary>
        internal static double SiteLongitude
        {
            get
            {
                LogMessage("SiteLongitude Get", "Returning 0.0 to ensure that SiderealTime method is functional out of the box.");
                return 0.0;
            }
            set
            {
                LogMessage("SiteLongitude Set", "Not implemented");
                throw new PropertyNotImplementedException("SiteLongitude", true);
            }
        }

        /// <summary>
        /// Specifies a post-slew settling time (sec.).
        /// </summary>
        internal static short SlewSettleTime
        {
            get
            {
                LogMessage("SlewSettleTime Get", "Not implemented");
                throw new PropertyNotImplementedException("SlewSettleTime", false);
            }
            set
            {
                LogMessage("SlewSettleTime Set", "Not implemented");
                throw new PropertyNotImplementedException("SlewSettleTime", true);
            }
        }

        /// <summary>
        /// Move the telescope to the given local horizontal coordinates, return when slew is complete
        /// </summary>
        internal static void SlewToAltAz(double Azimuth, double Altitude)
        {
            LogMessage("SlewToAltAz", "Not implemented");
            throw new MethodNotImplementedException("SlewToAltAz");
        }


        /// <summary>
        /// This Method must be implemented if <see cref="CanSlewAltAzAsync" /> returns True.
        /// It returns immediately, with Slewing set to True
        /// </summary>
        /// <param name="Azimuth">Azimuth to which to move</param>
        /// <param name="Altitude">Altitude to which to move to</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "VSTHRD200:Use \"Async\" suffix for async methods", Justification = "internal static method name used for many years.")]
        internal static void SlewToAltAzAsync(double Azimuth, double Altitude)
        {
            LogMessage("SlewToAltAzAsync", "Not implemented");
            throw new MethodNotImplementedException("SlewToAltAzAsync");
        }

        /// <summary>
        /// This Method must be implemented if <see cref="CanSlewAltAzAsync" /> returns True.
        /// It does not return to the caller until the slew is complete.
        /// </summary>
        internal static void SlewToCoordinates(double RightAscension, double Declination)
        {
            LogMessage("SlewToCoordinates", "Not implemented");
            throw new MethodNotImplementedException("SlewToCoordinates");
        }

        /// <summary>
        /// Move the telescope to the given equatorial coordinates, return with Slewing set to True immediately after starting the slew.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "VSTHRD200:Use \"Async\" suffix for async methods", Justification = "internal static method name used for many years.")]
        internal static void SlewToCoordinatesAsync(double RightAscension, double Declination)
        {
            LogMessage("SlewToCoordinatesAsync", "Not implemented");
            throw new MethodNotImplementedException("SlewToCoordinatesAsync");
        }

        /// <summary>
        /// Move the telescope to the <see cref="TargetRightAscension" /> and <see cref="TargetDeclination" /> coordinates, return when slew complete.
        /// </summary>
        internal static void SlewToTarget()
        {
            LogMessage("SlewToTarget", "Not implemented");
            throw new MethodNotImplementedException("SlewToTarget");
        }

        /// <summary>
        /// Move the telescope to the <see cref="TargetRightAscension" /> and <see cref="TargetDeclination" />  coordinates,
        /// returns immediately after starting the slew with Slewing set to True.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "VSTHRD200:Use \"Async\" suffix for async methods", Justification = "internal static method name used for many years.")]
        internal static void SlewToTargetAsync()
        {
            LogMessage("SlewToTargetAsync", "Not implemented");
            throw new MethodNotImplementedException("SlewToTargetAsync");
        }

        /// <summary>
        /// True if telescope is in the process of moving in response to one of the
        /// Slew methods or the <see cref="MoveAxis" /> method, False at all other times.
        /// </summary>
        internal static bool Slewing
        {
            get
            {
                try
                {
                    string s;
                    s = SharedResources.SendMessage("CS#");
                    //s = s.Substring(0, s.Length - 1);
                    s = HelperClass1.GetUntil(s, '#');
                    bool ret = s.Equals("TRUE", StringComparison.Ordinal);
                    tl.LogMessage("Slewing", "Get - " + ret.ToString());
                    return ret;
                }
                catch (Exception ex)
                {
                    LogMessage("Slewing get task", $"Exception - {ex.Message}\r\n{ex}");
                    throw;
                }
            }
        }

        /// <summary>
        /// Matches the scope's local horizontal coordinates to the given local horizontal coordinates.
        /// </summary>
        internal static void SyncToAltAz(double Azimuth, double Altitude)
        {
            LogMessage("SyncToAltAz", "Not implemented");
            throw new MethodNotImplementedException("SyncToAltAz");
        }

        /// <summary>
        /// Matches the scope's equatorial coordinates to the given equatorial coordinates.
        /// </summary>
        internal static void SyncToCoordinates(double RightAscension, double Declination)
        {
            LogMessage("SyncToCoordinates", "Not implemented");
            throw new MethodNotImplementedException("SyncToCoordinates");
        }

        /// <summary>
        /// Matches the scope's equatorial coordinates to the target equatorial coordinates.
        /// </summary>
        internal static void SyncToTarget()
        {
            LogMessage("SyncToTarget", "Not implemented");
            throw new MethodNotImplementedException("SyncToTarget");
        }

        /// <summary>
        /// The declination (degrees, positive North) for the target of an equatorial slew or sync operation
        /// </summary>
        internal static double TargetDeclination
        {
            get
            {
                LogMessage("TargetDeclination Get", "Not implemented");
                throw new PropertyNotImplementedException("TargetDeclination", false);
            }
            set
            {
                LogMessage("TargetDeclination Set", "Not implemented");
                throw new PropertyNotImplementedException("TargetDeclination", true);
            }
        }

        /// <summary>
        /// The right ascension (hours) for the target of an equatorial slew or sync operation
        /// </summary>
        internal static double TargetRightAscension
        {
            get
            {
                LogMessage("TargetRightAscension Get", "Not implemented");
                throw new PropertyNotImplementedException("TargetRightAscension", false);
            }
            set
            {
                LogMessage("TargetRightAscension Set", "Not implemented");
                throw new PropertyNotImplementedException("TargetRightAscension", true);
            }
        }

        /// <summary>
        /// The state of the telescope's sidereal tracking drive.
        /// </summary>
        internal static bool Tracking
        {
            get
            {
                try
                {
                    string s;
                    s = SharedResources.SendMessage("CT#");
                    //s = s.Substring(0, s.Length - 1);
                    s = HelperClass1.GetUntil(s, '#');
                    bool ret = s.Equals("TRUE", StringComparison.Ordinal);
                    tl.LogMessage("Tracking", "Get - " + ret.ToString());
                    return ret;
                }
                catch (Exception ex)
                {
                    LogMessage("Tracking get task", $"Exception - {ex.Message}\r\n{ex}");
                    throw;
                }
            }
            set
            {
                try
                {
                    if (value) // Tracking is being enabled
                    {
                        string s;
                        s = SharedResources.SendMessage("T#");
                        //s = s.Substring(0, s.Length - 1);
                        s = HelperClass1.GetUntil(s, '#');
                        bool ret = s.Equals("TRUE", StringComparison.Ordinal);
                        tl.LogMessage("Tracking", "Set - " + ret.ToString());
                        return;
                    }
                    else // Tracking is being disabled
                    {
                        string s;
                        s = SharedResources.SendMessage("TS#");
                        //s = s.Substring(0, s.Length - 1);
                        s = HelperClass1.GetUntil(s, '#');
                        bool ret = s.Equals("TRUE", StringComparison.Ordinal);
                        tl.LogMessage("Tracking", "Set - " + ret.ToString());
                        return;
                    }
                
                }
                catch (Exception ex)
                {
                    LogMessage("Tracking set task", $"Exception - {ex.Message}\r\n{ex}");
                    throw;
                }
            }
        }

        /// <summary>
        /// The current tracking rate of the telescope's sidereal drive
        /// </summary>
        internal static DriveRates TrackingRate
        {
            get
            {
                const DriveRates DEFAULT_DRIVERATE = DriveRates.driveSidereal;
                LogMessage("TrackingRate Get", $"{DEFAULT_DRIVERATE}");
                return DEFAULT_DRIVERATE;
            }
            set
            {
                LogMessage("TrackingRate Set", "Not implemented");
                throw new PropertyNotImplementedException("TrackingRate", true);
            }
        }

        /// <summary>
        /// Returns a collection of supported <see cref="DriveRates" /> values that describe the permissible
        /// values of the <see cref="TrackingRate" /> property for this telescope type.
        /// </summary>
        internal static ITrackingRates TrackingRates
        {
            get
            {
                ITrackingRates trackingRates = new TrackingRates();
                LogMessage("TrackingRates", "Get - ");
                foreach (DriveRates driveRate in trackingRates)
                {
                    LogMessage("TrackingRates", "Get - " + driveRate.ToString());
                }
                return trackingRates;
            }
        }

        /// <summary>
        /// The UTC date/time of the telescope's internal clock
        /// </summary>
        internal static DateTime UTCDate
        {
            get
            {
                DateTime utcDate = DateTime.UtcNow;
                LogMessage("UTCDate", "Get - " + String.Format("MM/dd/yy HH:mm:ss", utcDate));
                return utcDate;
            }
            set
            {
                LogMessage("UTCDate Set", "Not implemented");
                throw new PropertyNotImplementedException("UTCDate", true);
            }
        }

        /// <summary>
        /// Takes telescope out of the Parked state.
        /// </summary>
        internal static void Unpark()
        {
            LogMessage("Unpark", "Not implemented");
            throw new MethodNotImplementedException("Unpark");
        }

        #endregion

        #region Private properties and methods
        // Useful methods that can be used as required to help with driver development

        /// <summary>
        /// Returns true if there is a valid connection to the driver hardware
        /// </summary>
        private static bool IsConnected
        {
            get
            {
                // TODO check that the driver hardware connection exists and is connected to the hardware
                return connectedState;
            }
        }

        /// <summary>
        /// Use this function to throw an exception if we aren't connected to the hardware
        /// </summary>
        /// <param name="message"></param>
        private static void CheckConnected(string message)
        {
            if (!IsConnected)
            {
                throw new NotConnectedException(message);
            }
        }

        /// <summary>
        /// Read the device configuration from the ASCOM Profile store
        /// </summary>
        internal static void ReadProfile()
        {
            using (Profile driverProfile = new Profile())
            {
                driverProfile.DeviceType = "Telescope";
                tl.Enabled = Convert.ToBoolean(driverProfile.GetValue(DriverProgId, traceStateProfileName, string.Empty, traceStateDefault));
                comPort = driverProfile.GetValue(DriverProgId, comPortProfileName, string.Empty, comPortDefault);
            }
        }

        /// <summary>
        /// Write the device configuration to the  ASCOM  Profile store
        /// </summary>
        internal static void WriteProfile()
        {
            using (Profile driverProfile = new Profile())
            {
                driverProfile.DeviceType = "Telescope";
                driverProfile.WriteValue(DriverProgId, traceStateProfileName, tl.Enabled.ToString());
                driverProfile.WriteValue(DriverProgId, comPortProfileName, comPort.ToString());
            }
        }

        /// <summary>
        /// Log helper function that takes identifier and message strings
        /// </summary>
        /// <param name="identifier"></param>
        /// <param name="message"></param>
        internal static void LogMessage(string identifier, string message)
        {
            tl.LogMessageCrLf(identifier, message);
        }

        /// <summary>
        /// Log helper function that takes formatted strings and arguments
        /// </summary>
        /// <param name="identifier"></param>
        /// <param name="message"></param>
        /// <param name="args"></param>
        internal static void LogMessage(string identifier, string message, params object[] args)
        {
            var msg = string.Format(message, args);
            LogMessage(identifier, msg);
        }
        #endregion
    }
}

