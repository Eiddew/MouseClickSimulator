﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

using TTMouseclickSimulator.Core;
using TTMouseclickSimulator.Core.Actions;
using TTMouseclickSimulator.Core.Environment;
using TTMouseclickSimulator.Core.ToontownRewritten.Environment;
using TTMouseclickSimulator.Project;
using TTMouseclickSimulator.Utils;

namespace TTMouseclickSimulator
{
    public partial class MainWindow : Window
    {
        private const string AppName = "TTR Mouse Click Simulator";

        private SimulatorProject project;
        private SimulatorConfiguration.QuickActionDescriptor currentQuickAction;
        private Button[] quickActionButtons;

        private Simulator simulator;
        // Callbacks that we need to call when we start or stop the simulator.
        private Action simulatorStartAction, simulatorStopAction;

        /// <summary>
        /// If true, the window should be closed after the simulator stopped.s
        /// </summary>
        private bool closeWindowAfterStop;

        /// <summary>
        /// The file extension for Simulator Project files. Currently we use ".xml".
        /// </summary>
        private const string ProjectFileExtension = ".xml";
        private const string SampleProjectsFolderName = "SampleProjects";

        private readonly Microsoft.Win32.OpenFileDialog openFileDialog;


        private const string actionTitleMainAction = "Main Action";


        public MainWindow()
        {
            InitializeComponent();

            lblAppName.Content = AppName;
            Title = AppName;

            openFileDialog = new Microsoft.Win32.OpenFileDialog();
            openFileDialog.DefaultExt = ProjectFileExtension;
            openFileDialog.Filter = "XML Simulator Project|*" + ProjectFileExtension;
            // Set the initial directory to the executable path or the "SampleProjects" folder if it exists.
            string exeDirectory = System.IO.Path.GetDirectoryName(Assembly.GetEntryAssembly().Location);
            string sampleProjectsPath = System.IO.Path.Combine(exeDirectory, SampleProjectsFolderName);
            if (Directory.Exists(sampleProjectsPath))
                openFileDialog.InitialDirectory = sampleProjectsPath;
            else
                openFileDialog.InitialDirectory = exeDirectory;

            RefreshProjectControls();
        }

        private async void btnStart_Click(object sender, RoutedEventArgs e)
        {
            await RunSimulatorAsync();
        }

        private async Task RunSimulatorAsync()
        {
            btnStart.IsEnabled = false;
            btnStop.IsEnabled = true;
            btnLoad.IsEnabled = false;
            if (quickActionButtons != null)
                foreach (var bt in quickActionButtons)
                    bt.IsEnabled = false;



            // Run the simulator in another task so it is not executed in the GUI thread.
            // However, we then await that new task so we are notified when it is finished.
            Simulator sim = simulator = new Simulator(currentQuickAction != null ? currentQuickAction.Action 
                : project.Configuration.MainAction, TTRWindowsEnvironment.Instance);
            sim.AsyncRetryHandler = async (ex) => !closeWindowAfterStop && await HandleSimulatorRetryAsync(sim, ex);

            Exception runException = null;
            simulatorStartAction?.Invoke();
            await Task.Run(async () =>
            {
                try
                {
                    await sim.RunAsync();
                }
                catch (Exception ex)
                {
                    runException = ex;   
                }
            });
            simulatorStopAction?.Invoke();

            // Don't show a messagebox if we need to close the window.
            if (!closeWindowAfterStop && runException != null && !(runException is SimulatorCanceledException))
            {
                TaskDialog dialog = new TaskDialog()
                {
                    Title = AppName,
                    MainInstruction = "Simulator stopped!",
                    Content = runException.Message,
                    ExpandedInformation = GetExceptionDetailsText(runException),
                    MainIcon = TaskDialog.TaskDialogIcon.Stop,
                    CommonButtons = TaskDialog.TaskDialogButtons.OK
                };
                dialog.Flags |= TaskDialog.TaskDialogFlags.ExpandFooterArea;
                dialog.Show(this);
            }

            HandleSimulatorCanceled();
        }

        private async Task<bool> HandleSimulatorRetryAsync(Simulator sim, Exception ex)
        {
            // Show a TaskDialog.
            bool result = false;
            await Dispatcher.InvokeAsync(new Action(() =>
            {
                if (!closeWindowAfterStop)
                {
                    MessageBox.Show(ex.ToString(), "Verbose Error");
                    return;
                    /*
                    TaskDialog dialog = new TaskDialog()
                    {
                        Title = AppName,
                        MainInstruction = "Simulator interrupted!",
                        Content = ex.Message,
                        ExpandedInformation = GetExceptionDetailsText(ex),
                        MainIcon = TaskDialog.TaskDialogIcon.Warning,
                        CommonButtons = TaskDialog.TaskDialogButtons.Cancel
                    };
                    dialog.Flags |= TaskDialog.TaskDialogFlags.UseCommandLinks |
                            TaskDialog.TaskDialogFlags.ExpandFooterArea;

                    var buttonTryAgain = dialog.CreateCustomButton("Try again\n" 
                        + "The Simulator will try to run the current action again.");
                    var buttonStop = dialog.CreateCustomButton("Stop the Simulator");

                    dialog.CustomButtons = new TaskDialog.ICustomButton[] { buttonTryAgain, buttonStop };
                    dialog.DefaultCustomButton = buttonStop;

                    dialog.Show(this);

                    if (dialog.ResultCustomButton == buttonTryAgain)
                        result = true;
                    */
                }
            }));

            return result;
        }

        private static string GetExceptionDetailsText(Exception ex)
        {
            StringBuilder detailsSb = null;
            Exception innerEx = ex.InnerException;
            while (innerEx != null)
            {
                if (detailsSb == null)
                    detailsSb = new StringBuilder();
                else
                    detailsSb.Append("\n\n");

                detailsSb.Append(innerEx.Message);

                innerEx = innerEx.InnerException;
            }

            return detailsSb?.ToString();
        }

        private void HandleSimulatorCanceled()
        {
            simulator = null;
            btnStart.IsEnabled = true;
            btnStop.IsEnabled = false;
            btnLoad.IsEnabled = true;
            if (quickActionButtons != null)
                foreach (var bt in quickActionButtons)
                    bt.IsEnabled = true;

            if (currentQuickAction != null)
            {
                currentQuickAction = null;
                RefreshProjectControls();
            }

            if (closeWindowAfterStop)
                Close();
        }



        private void btnStop_Click(object sender, RoutedEventArgs e)
        {
            simulator.Cancel();
            btnStop.IsEnabled = false;
        }

        private void btnLoad_Click(object sender, RoutedEventArgs e)
        {
            if (openFileDialog.ShowDialog(this) == true)
            {
                // Try to load the given project.
                try
                {
                    using (FileStream fs = new FileStream(openFileDialog.FileName, FileMode.Open,
                        FileAccess.Read, FileShare.Read))
                    {
                        XmlProjectDeserializer deser = new XmlProjectDeserializer();
                        project = deser.Deserialize(fs);
                    }
                }
                catch (Exception ex)
                {
                    TaskDialog dialog = new TaskDialog()
                    {
                        Title = AppName,
                        MainInstruction = "Could not load the selected project.",
                        Content = ex.Message,
                        ExpandedInformation = GetExceptionDetailsText(ex),
                        MainIcon = TaskDialog.TaskDialogIcon.SecurityErrorBar,
                        MainUpdateIcon = TaskDialog.TaskDialogIcon.Stop,
                        CommonButtons = TaskDialog.TaskDialogButtons.OK
                    };
                    dialog.Flags |=  TaskDialog.TaskDialogFlags.SizeToContent |
                        TaskDialog.TaskDialogFlags.ExpandFooterArea;

                    dialog.Show(this);
                    return;
                }


                if (quickActionButtons != null)
                {
                    foreach (var b in quickActionButtons)
                        gridProjectControls.Children.Remove(b);
                    quickActionButtons = null;
                }

                RefreshProjectControls();

                // For each quick action, create a button.
                quickActionButtons = new Button[project.Configuration.QuickActions.Count];
                for (int idx = 0; idx < project.Configuration.QuickActions.Count; idx++)
                {
                    int i = idx;
                    var quickAction = project.Configuration.QuickActions[i];

                    Button b = quickActionButtons[i] = new Button();
                    b.Height = 21;
                    b.HorizontalAlignment = HorizontalAlignment.Left;
                    b.VerticalAlignment = VerticalAlignment.Top;
                    b.Margin = new Thickness(0, 2 + 23 * i, 0, 0);
                    b.Content = "  " + quickAction.Name + "  ";
                    gridProjectControls.Children.Add(b);
                    Grid.SetRow(b, 1);

                    b.Click += async (_s, _e) =>
                    {
                        currentQuickAction = quickAction;
                        RefreshProjectControls();

                        await RunSimulatorAsync();
                    };
                }
            }
        }

        private void RefreshProjectControls()
        {
            lblActionTitle.Content = currentQuickAction != null ? currentQuickAction.Name 
                : project?.Configuration.MainAction != null ? actionTitleMainAction : "";
            if (project == null)
            {
                lblCurrentProject.Content = "(none)";
                txtDescription.Text = string.Empty;
                btnStart.IsEnabled = false;
            }
            else 
            {
                lblCurrentProject.Content = project.Title;
                txtDescription.Text = project.Description;
                btnStart.IsEnabled = project.Configuration.MainAction != null;

                // Create labels for each action.
                actionListGrid.Children.Clear();
                IAction mainAct = currentQuickAction != null ? currentQuickAction.Action 
                    : project.Configuration.MainAction;
                if (mainAct != null)
                {
                    int posCounter = 0;
                    CreateActionLabels(mainAct, actionListGrid, 0, ref posCounter,
                        out simulatorStartAction, out simulatorStopAction);
                }
            }
        }

        private void CreateActionLabels(IAction action, Grid grid, int recursiveCount, 
            ref int posCounter, out Action handleStart, out Action handleStop)
        {
            Label l = new Label();
            l.Margin = new Thickness(recursiveCount * 10, 18 * posCounter, 0, 0);
            grid.Children.Add(l);

            string str = action.ToString();
            l.Content = str;

            handleStart = () => {
                l.Foreground = new SolidColorBrush(Color.FromArgb(255, 29, 134, 184));
            };

            handleStop = () =>
            {
                l.Foreground = new SolidColorBrush(Color.FromArgb(255, 0, 0, 0));
                l.Content = str;
            };

            action.ActionInformationUpdated += s => Dispatcher.Invoke(new Action(() => l.Content = str + " – " + s));

            posCounter++;

            if (action is IActionContainer)
            {
                
                IActionContainer cont = (IActionContainer)action;
                IList<IAction> subActions = cont.SubActions;
                Action[] handleStartActions = new Action[subActions.Count];
                Action[] handleStopActions = new Action[subActions.Count];
                for (int i = 0; i < subActions.Count; i++)
                {
                    CreateActionLabels(subActions[i], grid, recursiveCount + 1, ref posCounter,
                        out handleStartActions[i], out handleStopActions[i]);
                }

                int? currentActiveAction = null;
                cont.SubActionStartedOrStopped += (idx) => Dispatcher.Invoke(new Action(() =>
                {
                    if (idx.HasValue)
                    {
                        currentActiveAction = idx;
                        handleStartActions[idx.Value]();
                    }
                    else if (currentActiveAction.HasValue)
                    {
                        handleStopActions[currentActiveAction.Value]();
                    }
                }));
            }
        }

        

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // If the simulator is currently running, don't close the window but stop the
            // simulator and wait until it is finished.
            if (simulator != null)
            {
                e.Cancel = true;
                Hide();

                closeWindowAfterStop = true;
                simulator.Cancel();
            }
        }
    }
}
