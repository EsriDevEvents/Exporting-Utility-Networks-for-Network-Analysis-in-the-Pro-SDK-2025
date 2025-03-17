using ArcGIS.Core.Data;
using ArcGIS.Desktop.Framework;
using ArcGIS.Desktop.Framework.Contracts;
using ArcGIS.Desktop.Framework.Dialogs;
using ArcGIS.Desktop.Framework.Threading.Tasks;
using ArcGIS.Desktop.Mapping;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using ArcGIS.Core.Data.UtilityNetwork;
using ArcGIS.Core.Data.UtilityNetwork.Trace;
using ArcGIS.Desktop.Mapping.Events;
using Element = ArcGIS.Core.Data.UtilityNetwork.Element;
using TraceConfiguration = ArcGIS.Core.Data.UtilityNetwork.Trace.TraceConfiguration;


namespace NetworkAnalysis
{
  internal class NetworkAnalyticsDockPaneViewModel : DockPane
  {
    #region private helpers
    private const string _dockPaneID = "NetworkAnalysis_NetworkAnalyticsDockPane";

    private string _jsonExportPath = string.Empty;
    private string _quickInfoText = string.Empty;
    private string _analysisText = string.Empty;
    private string _subnetworkName = string.Empty;
    private string _jsonLoadPath = string.Empty;

    private ICommand _showQuickInfoCommand = null;
    private ICommand _loadJsonCommand = null;
    private ICommand _exportJsonCommand = null;

    public string JsonExportPath
    {
      get
      {
        return _jsonExportPath;
      }
      set
      {
        SetProperty(ref _jsonExportPath, value, () => JsonExportPath);
      }
    }

    public string JsonLoadPath
    {
      get
      {
        return _jsonLoadPath;
      }
      set
      {
        SetProperty(ref _jsonLoadPath, value, () => JsonLoadPath);

      }
    }

    public string QuickInfoText
    {
      get
      {
        return _quickInfoText;
      }
      set
      {
        SetProperty(ref _quickInfoText, value, () => QuickInfoText);
      }
    }

    public string AnalysisText
    {
      get
      {
        return _analysisText;
      }
      set
      {
        SetProperty(ref _analysisText, value, () => AnalysisText);
      }
    }

    public string SubnetworkName
    {
      get
      {
        return _subnetworkName;
      }
      set
      {
        SetProperty(ref _subnetworkName, value, () => SubnetworkName);
      }
    }

    private ObservableCollection<string> _subnetworkNames;

    public ObservableCollection<string> SubnetworkNames
    {
      get
      {
        return _subnetworkNames;
      }
      set
      {
        _subnetworkNames = value;
      }
    }

    public ICommand ShowQuickInfoCommand
    {
      get
      {
        _showQuickInfoCommand ??= new RelayCommand((param) => ShowQuickInfo(param), () => true);
        return _showQuickInfoCommand;
      }
    }

    public ICommand ExportJsonCommand
    {
      get
      {
        _exportJsonCommand ??= new RelayCommand(ExportJson);
        return _exportJsonCommand;
      }
    }

    public ICommand LoadJsonCommand
    {
      get
      {
        _loadJsonCommand ??= new RelayCommand(LoadJson);
        return _loadJsonCommand;
      }
    }

    #endregion

    private async void ShowQuickInfo(object subnetworkName)
    {
      int explicitEdges = 0;
      int edgeFeatures = 0;
      int junctionFeatures = 0;
      int systemJunctionFeatures = 0;
      long shapeLength = 0;

      await QueuedTask.Run(() =>
      {
        UtilityNetworkLayer utilityNetworkLayer = MapView.Active.Map.GetLayersAsFlattenedList().
        OfType<UtilityNetworkLayer>().FirstOrDefault();

        if (utilityNetworkLayer == null)
        {
          MessageBox.Show("UN Layer not found");
          return;
        }

        using (UtilityNetwork utilityNetwork = utilityNetworkLayer.GetUtilityNetwork())
        using (UtilityNetworkDefinition utilityNetworkDefinition = utilityNetwork.GetDefinition())
        using (SubnetworkManager subnetworkManager = utilityNetwork.GetSubnetworkManager())
        using (TraceManager traceManager = utilityNetwork.GetTraceManager())
        using (NetworkSource waterDevicenetworkSource = GetNetworkSource(utilityNetworkDefinition, "Water Device"))
        using (FeatureClass waterDeviceFeatureClass = utilityNetwork.GetTable(waterDevicenetworkSource) as FeatureClass)
        using (FeatureClassDefinition waterDeviceFeatureClassDefn = waterDeviceFeatureClass.GetDefinition())
        {
          DomainNetwork domainNetwork = utilityNetworkDefinition.GetDomainNetwork("Water");
          Tier pressureTier = domainNetwork.GetTier("Water Pressure");
          Subnetwork subnetwork = subnetworkManager.GetSubnetwork(subnetworkName.ToString());

          SubnetworkStates status = subnetwork.GetState();
          if (status == SubnetworkStates.Dirty)
          {
            subnetwork.Update();
          }

          // Network attributes 
          List<string> networkAttributeNames = new List<string>();
          IReadOnlyList<NetworkAttribute> networkAttributes = utilityNetworkDefinition.GetNetworkAttributes();
          foreach (NetworkAttribute networkAttribute in networkAttributes)
          {
            networkAttributeNames.Add(networkAttribute.Name);
          }

          // List of additional fields to pull during the trace operation
          List<string> deviceFields = waterDeviceFeatureClassDefn.GetFields().Select(f => f.Name).ToList();

          // Function to calculate shape
          NetworkAttribute shapeLengthAttribute = utilityNetworkDefinition.GetNetworkAttribute("Shape length");
          Function summingFunction = new Add(shapeLengthAttribute);

          // Get trace configuration from the tier for the subnetwork trace
          TraceConfiguration traceConfiguration = pressureTier.GetTraceConfiguration();
          traceConfiguration.Functions = new List<Function>() { summingFunction };

          // Set result options with network attributes and additional attribute fields to fetch during trace
          ResultOptions resultOptions = new ResultOptions()
          {
            IncludeGeometry = true,
            NetworkAttributes = networkAttributeNames,
            ResultFields = new Dictionary<NetworkSource, List<string>> { { waterDevicenetworkSource, deviceFields } }
          };

          // Set trace argument
          TraceArgument traceArgument = new TraceArgument(subnetwork)
          {
            Configuration = traceConfiguration,
            ResultTypes = new List<ResultType>() { ResultType.Feature, ResultType.FunctionValue},
            //ResultOptions = resultOptions // Disabled for quick visuals for the demo 
          };

          // Get tracer from the trace manager
          SubnetworkTracer subnetworkTracer = traceManager.GetTracer<SubnetworkTracer>();

          try
          {
            // Execute trace
            IReadOnlyList<Result> traceResults = subnetworkTracer.Trace(traceArgument);

            #region Export trace
            /*
            TraceExportOptions traceExportOptions = new TraceExportOptions()
            {
              ServiceSynchronizationType = ServiceSynchronizationType.Asynchronous,
              IncludeDomainDescriptions = true
            };

            subnetworkTracer.Export(outputJsonPath: new Uri("JsonPath"),  traceArgument, traceExportOptions);
            */
            #endregion

            #region Process trace results
            Dictionary<string, long> networkElements = new Dictionary<string, long>();
            foreach (Result traceResult in traceResults)
            {
              if (traceResult is FeatureElementResult featureElementResult)
              {
                foreach (FeatureElement featureElement in featureElementResult.FeatureElements)
                {

                  if (networkElements.ContainsKey(featureElement.NetworkSource.Name))
                  {
                    networkElements[featureElement.NetworkSource.Name] = ++networkElements[featureElement.NetworkSource.Name];
                  }
                  else
                  {
                    networkElements.Add(featureElement.NetworkSource.Name, 1);
                  }

                  switch (featureElement.NetworkSource.Type)
                  {
                    case SourceType.Association:
                      explicitEdges++;
                      break;
                    case SourceType.Edge:
                      edgeFeatures++;
                      break;
                    case SourceType.Junction:
                      junctionFeatures++;
                      break;
                    case SourceType.SystemJunction:
                      systemJunctionFeatures++;
                      break;
                  }
                }
              }

              if (traceResult is FunctionOutputResult functionOutputResult)
              {
                FunctionOutput functionOutput = functionOutputResult.FunctionOutputs.First();
                shapeLength = Convert.ToInt64(functionOutput.Value);
              }
            }
            #endregion

            SubnetworkName = subnetwork.Name;

            QuickInfoText = $"Total edge features #{edgeFeatures:N0} " +
            $"\nTotal junction features #{junctionFeatures:N0} " +
            $"\nTotal edge shape length #{shapeLength:N0} " +
            $"\nWater Devices #{networkElements["WaterDevice"]:N0} " +
            $"\nWater Line #{networkElements["WaterLine"]:N0} " +
            $"\nWater Junction #{networkElements["WaterJunction"]:N0}";

            #region Display selection on map
            Dictionary<MapMember, List<long>> selectionDictionary = new Dictionary<MapMember, List<long>>();

            foreach (Result traceResult in traceResults)
            {
              if (traceResult is FeatureElementResult featureElementResult)
              {
                IEnumerable<Element> elements = featureElementResult.FeatureElements;
                foreach (Element element in elements)
                {
                  Table table1 = utilityNetwork.GetTable(element.NetworkSource);
                  string tableName = table1.GetName();
                  FeatureLayer featLayer = MapView.Active.Map.GetLayersAsFlattenedList().OfType<FeatureLayer>().FirstOrDefault(f => f.GetFeatureClass().GetName().Contains(tableName));

                  if (selectionDictionary.ContainsKey(featLayer))
                  {
                    selectionDictionary[featLayer].Add(element.ObjectID);
                  }
                  else
                  {
                    selectionDictionary.Add(featLayer, new List<long>() { element.ObjectID });
                  }
                }

                SelectionSet selectionSet = SelectionSet.FromDictionary(selectionDictionary);
                MapView.Active.Map.SetSelection(selectionSet);
                MapView.Active.Redraw(true);
              }
            }
            #endregion
          }
          catch (Exception ex)
          {
            MessageBox.Show(ex.Message);
          }
        }
      });

    }

    private async void ExportJson()
    {
      await QueuedTask.Run(() =>
      {
        UtilityNetworkLayer utilityNetworkLayer = MapView.Active.Map.GetLayersAsFlattenedList().
        OfType<UtilityNetworkLayer>().FirstOrDefault();

        if (utilityNetworkLayer == null)
        {
          MessageBox.Show("UN Layer not found");
          return;
        }

        using (UtilityNetwork utilityNetwork = utilityNetworkLayer.GetUtilityNetwork())
        using (UtilityNetworkDefinition utilityNetworkDefinition = utilityNetwork.GetDefinition())
        using (SubnetworkManager subnetworkManager = utilityNetwork.GetSubnetworkManager())
        using (TraceManager traceManager = utilityNetwork.GetTraceManager())
        using (NetworkSource networkSource = GetNetworkSource(utilityNetworkDefinition, "Water Device"))
        using (NetworkSource waterDevicenetworkSource = GetNetworkSource(utilityNetworkDefinition, "Water Device"))
        using (FeatureClass waterDeviceFeatureClass = utilityNetwork.GetTable(waterDevicenetworkSource) as FeatureClass)
        using (FeatureClassDefinition waterDeviceFeatureClassDefn = waterDeviceFeatureClass.GetDefinition())
        {
          try
          {
            DomainNetwork domainNetwork = utilityNetworkDefinition.GetDomainNetwork("Water");
            Subnetwork subnetwork = subnetworkManager.GetSubnetwork(SubnetworkName);

            // List of additional fields to pull during the trace operation
            List<string> deviceFields = waterDeviceFeatureClassDefn.GetFields().Select(f => f.Name).ToList();

            // Set export option param
            SubnetworkExportOptions exportOptions = new SubnetworkExportOptions()
            {
              IncludeDomainDescriptions = true,
              IncludeGeometry = true,
              SetAcknowledged = false,

              // Network attributes to export 
              ResultNetworkAttributes = [.. utilityNetworkDefinition.GetNetworkAttributes()],

              // Additional attribute fields to export
              ResultFieldsByNetworkSourceID = new Dictionary<int, List<string>> { { waterDevicenetworkSource.ID, deviceFields } },

              // Results types to export 
              SubnetworkExportResultTypes = [SubnetworkExportResultType.Features,
                SubnetworkExportResultType.ContainmentAndAttachment,
                SubnetworkExportResultType.Connectivity]
            };

            subnetwork.Export(new Uri(JsonExportPath), exportOptions);
          }
          catch (Exception ex)
          {
            MessageBox.Show(ex.Message);
          }
        }
      });
    }


    private async void LoadJson()
    {
      var parseResult = TraceParser.ParseJsonExport(JsonLoadPath, true);
      Dictionary<string, List<string>> connectivityResults = parseResult.connectivityResults;
      Dictionary<string, Dictionary<string, object>> featureResults = parseResult.featureResults;

      List<string> barrierNodes = TraceParser.GetBarriers(featureResults);
      List<string> subnetworkControllerNodes = TraceParser.GetSubnetworkController(featureResults);

      AnalysisText = $"{parseResult.parseSummary} Barriers #{barrierNodes.Count} \n SubnetworkControllers #{subnetworkControllerNodes.Count}";

      // Undirected graph
      INetworkGraph unDirectedNetworkGraph = GraphSolver.CreateNetworkGraph(connectivityResults, false);

      List<string> downstreamTraceElements = GraphSolver.DownstreamTrace(unDirectedNetworkGraph, new List<string>(subnetworkControllerNodes));

      // Example call to ForwardStar.
      List<(string start, List<string> result, List<string> barriers)> forwardStarResults = GraphSolver.ForwardStar(subnetworkControllerNodes, unDirectedNetworkGraph, barrierNodes);


      // Directed graph
      INetworkGraph directedNetworkGraph = GraphSolver.CreateNetworkGraph(connectivityResults, true);

      List<string> downstreamDirectedTraceElements = GraphSolver.DownstreamTrace(directedNetworkGraph, new List<string>(subnetworkControllerNodes));

      // Example call to ForwardStar.
      List<(string start, List<string> result, List<string> barriers)> forwardStarDirectedResults = GraphSolver.ForwardStar(subnetworkControllerNodes, directedNetworkGraph, barrierNodes);

    }

    private NetworkSource GetNetworkSource(UtilityNetworkDefinition unDefinition, string name)
    {
      IReadOnlyList<NetworkSource> allSources = unDefinition.GetNetworkSources();
      foreach (NetworkSource source in allSources)
      {
        if (name.Contains("Partitioned Sink"))
        {
          if (source.Name.Replace(" ", "").ToUpper().Contains(name.Replace(" ", "").ToUpper()) ||
              source.Name.Replace(" ", "").ToUpper().Contains(name.Replace("Partitioned Sink", "Part_Sink").Replace(" ", "").ToUpper()))
          {
            return source;
          }
        }
        if (name.Contains("Hierarchical Sink"))
        {
          if (source.Name.Replace(" ", "").ToUpper().Contains(name.Replace(" ", "").ToUpper()) ||
              source.Name.Replace(" ", "").ToUpper().Contains(name.Replace("Hierarchical Sink", "Hier_Sink").Replace(" ", "").ToUpper()))
          {
            return source;
          }
        }

        if (source.Name.Replace(" ", "").ToUpper().Contains(name.Replace(" ", "").ToUpper()))
        {
          return source;
        }
      }
      return null;
    }

    private string GetTableNameFromSourceName(UtilityNetwork un, UtilityNetworkDefinition unDefinition, string name)
    {
      using (NetworkSource networkSource = GetNetworkSource(unDefinition, name))
      {
        using (Table table = un.GetTable(networkSource))
        {
          return table.GetName();
        }
      }
    }

    private void OnMapSelectionChanged(MapSelectionChangedEventArgs eventArgs)
    {
      /*
      if (eventArgs.Selection.IsEmpty)
      {
        return;
      }

      QueuedTask.Run(() =>
      {
        var pp = eventArgs.Selection.ToDictionary();
        var keyValuePair = eventArgs.Selection.ToDictionary().Where(kvp => kvp.Key.Name == "Water Network").FirstOrDefault();
        var oidList = keyValuePair.Value;
        var mapLayer = keyValuePair.Key;

        QueryFilter filter = new QueryFilter() { ObjectIDs = new List<long>(oidList) };

        var unLayer =eventArgs.Map.Layers.Where(l => l.GetType().Equals(typeof(UtilityNetwork))).FirstOrDefault();


      });

      */
    }

    private void GetSelectedSubnetwork(QueryFilter filter)
    {
      QueuedTask.Run(() =>
      {
        UtilityNetworkLayer utilityNetworkLayer = MapView.Active.Map.GetLayersAsFlattenedList().OfType<UtilityNetworkLayer>().FirstOrDefault();

        if (utilityNetworkLayer == null)
        {
          MessageBox.Show("UN Layer not found");
          return;
        }

        using (UtilityNetwork utilityNetwork = utilityNetworkLayer.GetUtilityNetwork())
        using (UtilityNetworkDefinition utilityNetworkDefinition = utilityNetwork.GetDefinition()) { }

      });
    }

    private long GetAssetCount(FeatureClass featureClass, string fieldName, string fieldValue)
    {
      long count = 0;

      QueuedTask.Run(() =>
      {
        QueryFilter queryFilter = new QueryFilter()
        {
          WhereClause = $"{fieldName}  = `{fieldValue}`"
        };

        count = featureClass.GetCount(queryFilter);

      });

      return count;
    }

    protected NetworkAnalyticsDockPaneViewModel()
    {

      SubnetworkNames = new ObservableCollection<string>()
      {
        "PalmSpringsWaterSys1", "PalmSpringsWaterSys2", "PalmSpringsWaterSys3"
      };
    }

    /// <summary>
    /// Show the DockPane.
    /// </summary>
    internal static void Show()
    {
      DockPane pane = FrameworkApplication.DockPaneManager.Find(_dockPaneID);
      if (pane == null)
      {
        return;
      }

      pane.Activate();
    }

    protected override Task InitializeAsync()
    {
      MapSelectionChangedEvent.Subscribe(OnMapSelectionChanged, false);
      return base.InitializeAsync();
    }
  }

  /// <summary>
  /// Button implementation to show the DockPane.
  /// </summary>
  internal class NetworkAnalyticsDockPane_ShowButton : Button
  {
    protected override void OnClick()
    {
      NetworkAnalyticsDockPaneViewModel.Show();
    }
  }
}
