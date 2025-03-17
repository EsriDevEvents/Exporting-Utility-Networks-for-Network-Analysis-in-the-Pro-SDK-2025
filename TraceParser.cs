using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Text;
using System.Windows;
using Newtonsoft.Json;
using ArcGIS.Core.Data.UtilityNetwork;

namespace NetworkAnalysis
{
  public class TraceParser
  {

    public static (Dictionary<string, List<string>> connectivityResults, Dictionary<dynamic, dynamic> associationResults, Dictionary<string, Dictionary<string, object>> featureResults, Dictionary<int, string> sourceMapping, Dictionary<string, List<double>> points, Dictionary<object, object> lines, string parseSummary) ParseJsonExport(string jsonPath, bool complexGeometries = true)
    {
      StringBuilder sb = new StringBuilder();

      JArray featureElementArray = new JArray();
      JArray connectivityArray = new JArray();
      JArray associationArray = new JArray();
      JArray controllerArray = new JArray();
      JArray resultTypeArray = new JArray();
      Dictionary<int, string> sourceMapping = new Dictionary<int, string>();

      try
      {
        // Open the JSON file and load its content
        using (Stream fileStream = new FileStream(jsonPath, FileMode.Open))
        using (StreamReader streamReader = new StreamReader(fileStream))
        //Represents a reader that provides fast, non-cached, forward-only access to serialized JSON data
        using (JsonTextReader jsonReader = new JsonTextReader(streamReader))
        {
          JsonSerializer serializer = new JsonSerializer();

          // Read the opening object
          if (!jsonReader.Read())
          {
            return (null, null, null, null, null, null, sb.ToString());
          }

          if (jsonReader.TokenType != JsonToken.StartObject)
          {
            return (null, null, null, null, null, null, sb.ToString());
          }

          // Reads the next JSON token from the source
          while (jsonReader.Read())
          {
            if (jsonReader.TokenType != JsonToken.PropertyName)
            {
              continue;
            }

            if (jsonReader.Value is null)
            {
              return (null, null, null, null, null, null, sb.ToString());
            }

            string propertyName = jsonReader.Value.ToString();

            if (!jsonReader.Read())
            {
              return (null, null, null, null, null, null, sb.ToString());
            }

            switch (jsonReader.TokenType)
            {
              // Object start token
              case JsonToken.StartObject:
                if (propertyName == "sourceMapping")
                {
                  JObject sourceMappingObj = serializer.Deserialize<JObject>(jsonReader);
                  Debug.WriteLine(sourceMappingObj);
                  foreach (KeyValuePair<string, JToken> item in sourceMappingObj)
                  {
                    // Convert key from string to int and store the name
                    sourceMapping[int.Parse(item.Key)] = item.Value.ToString();
                  }
                }
                break;
              // Array start token
              case JsonToken.StartArray:
                switch (propertyName)
                {
                  case "featureElements":
                    {
                      while (jsonReader.TokenType != JsonToken.EndArray)
                      {
                        jsonReader.Read();
                        if (jsonReader.TokenType == JsonToken.EndArray)
                        {
                          break;
                        }

                        JObject element = serializer.Deserialize<JObject>(jsonReader);
                        featureElementArray.Add(element);
                      }
                      break;
                    }
                  case "connectivity":
                    {
                      while (jsonReader.TokenType != JsonToken.EndArray)
                      {
                        jsonReader.Read();
                        if (jsonReader.TokenType == JsonToken.EndArray)
                        {
                          break;
                        }
                        JObject element = serializer.Deserialize<JObject>(jsonReader);
                        connectivityArray.Add(element);
                      }
                      break;
                    }
                  case "associations":
                    {
                      while (jsonReader.TokenType != JsonToken.EndArray)
                      {
                        jsonReader.Read();
                        if (jsonReader.TokenType == JsonToken.EndArray)
                        {
                          break;
                        }
                        JObject element = serializer.Deserialize<JObject>(jsonReader);
                        associationArray.Add(element);
                      }
                      break;
                    }
                  case "controllers":
                    {
                      while (jsonReader.TokenType != JsonToken.EndArray)
                      {
                        jsonReader.Read();
                        if (jsonReader.TokenType == JsonToken.EndArray)
                        {
                          break;
                        }
                        JObject element = serializer.Deserialize<JObject>(jsonReader);
                        controllerArray.Add(element);
                      }

                      break;
                    }
                  case "resultTypes":
                    {
                      while (jsonReader.TokenType != JsonToken.EndArray)
                      {
                        jsonReader.Read();
                        if (jsonReader.TokenType == JsonToken.EndArray)
                        {
                          break;
                        }
                        JObject element = serializer.Deserialize<JObject>(jsonReader);
                        resultTypeArray.Add(element);
                      }
                      break;
                    }
                }
                break;
              default:
                Debug.WriteLine("Unhandled property: " + propertyName);
                break;
            }
          }
        }

        // Process featureElements
        var featureResult = ProcessFeatures(featureElementArray, sourceMapping, complexGeometries);
        Dictionary<string, Dictionary<string, object>> features = featureResult.features;
        Dictionary<string, List<double>> points = featureResult.points;
        Dictionary<dynamic, dynamic> lines = featureResult.lines;
        var featureSummary = featureResult.featureSummary;
        sb.AppendLine($"{featureSummary}");

        // Process connectivity
        var connectivityResult = ProcessConnectivity(connectivityArray, sourceMapping);
        Dictionary<string, List<string>> connectivities = connectivityResult.connectivities;
        Dictionary<dynamic, dynamic> connectivitiesGeometry = connectivityResult.connectivitiesGeometry;
        var connectivitySummary = connectivityResult.connectivitySummary;
        sb.AppendLine($"{connectivitySummary}");

        // Process associations
        var associationResult = ProcessAssociations(associationArray, sourceMapping);
        var associations = associationResult.associations;
        var associationsSummary = associationResult.associationSummary;
        sb.AppendLine($"{associationsSummary}");

        return (connectivities, associations, features, sourceMapping, points, lines, sb.ToString());
      }
      catch (Exception ex)
      {
        MessageBox.Show(ex.Message);
      }

      return (null, null, null, null, null, null, sb.ToString());
    }

    // Get barriers
    public static List<string> GetBarriers(Dictionary<string, Dictionary<string, object>> featureResults)
    {
      List<string> barriers = new List<string>();

      foreach (KeyValuePair<string, Dictionary<string, object>> featureResult in featureResults)
      {
        string key = featureResult.Key;
        Dictionary<string, object> values = featureResult.Value;

        if (!values.ContainsKey("assetGroupCode")) continue;
        if (Convert.ToInt32(values["assetGroupCode"]) != 2) continue;
        if (!values.ContainsKey("P:Device Status")) continue;
        int deviceValue = Convert.ToInt32(values["P:Device Status"]);

        // device = 0 - closed valve
        // device = 1 - open valve

        if (deviceValue != 0) continue;
        List<object> terminalIds = values["terminalIds"] as List<object>;

        if (terminalIds is not null && terminalIds.Count > 0)
        {
          //barriers.Add((values["networkSourceId"], values["globalId"], terminalIds.First().ToString()).ToString().Replace(" ", String.Empty));

          // Without terminal information
          barriers.Add((values["networkSourceId"], values["globalId"]).ToString().Replace(" ", String.Empty));
        }
      }

      return barriers;
    }

    // Get subnetwork controllers
    public static List<string> GetSubnetworkController(Dictionary<string, Dictionary<string, object>> featureResults)
    {
      List<string> subnetworkControllers = new List<string>();
      foreach (KeyValuePair<string, Dictionary<string, object>> featureResult in featureResults)
      {
        string key = featureResult.Key;
        Dictionary<string, object> values = featureResult.Value;

        if (values.ContainsKey("Is subnetwork controller"))
        {
          int isSubnetworkController = Convert.ToInt32(values["Is subnetwork controller"]);
          if (isSubnetworkController == 1)
          {
            List<object> terminalIds = values["terminalIds"] as List<object>;

            if (terminalIds is not null && terminalIds.Count > 0)
            {
              subnetworkControllers.Add((values["networkSourceId"], values["globalId"], terminalIds.First()).ToString().Replace(" ", String.Empty));
            }
          }
        }
      }

      return subnetworkControllers;
    }


    // Process features
    private static (Dictionary<string, Dictionary<string, object>> features, Dictionary<string, List<double>> points, Dictionary<dynamic, dynamic> lines, string featureSummary) ProcessFeatures(JToken featuresElement, Dictionary<int, string> sourceMapping, bool complexGeometries = true)
    {
      StringBuilder sb = new StringBuilder();

      // Initialize dictionaries for features, points and lines
      Dictionary<string, Dictionary<string, object>> features = new Dictionary<string, Dictionary<string, object>>();
      Dictionary<string, List<double>> points = new Dictionary<string, List<double>>();
      Dictionary<dynamic, dynamic> lines = new Dictionary<dynamic, dynamic>();

      sb.AppendLine($"Feature elements #{featuresElement.Count():n0}");

      // Create terminal_devices as features with "terminalId"
      List<JToken> terminalDevices = new List<JToken>();
      foreach (JToken feature in featuresElement)
      {
        if (feature["terminalId"] != null)
        {
          terminalDevices.Add(feature);
        }
      }
      // Calculate unique terminal devices by their (networkSourceId, globalId)
      HashSet<Tuple<object, object>> uniqueTerminalDevicesSet = new HashSet<Tuple<object, object>>();
      foreach (JToken feature in terminalDevices)
      {
        Tuple<object, object> key = Tuple.Create((object)feature["networkSourceId"], (object)feature["globalId"]);
        uniqueTerminalDevicesSet.Add(key);
      }
      int uniqueTerminalDevices = uniqueTerminalDevicesSet.Count;
      sb.AppendLine($"Duplicate entries for terminal devices#{(terminalDevices.Count - uniqueTerminalDevices):n0}");

      foreach (JToken element in featuresElement)
      {
        string featureKey = (element["networkSourceId"], element["globalId"]).ToString().Trim().Replace(" ", "");
        if (complexGeometries && features.ContainsKey(featureKey) && element["positionFrom"] == null)
        {
          continue;
        }

        Dictionary<string, object> featureValues = new Dictionary<string, object>();
        featureValues["networkSourceId"] = element["networkSourceId"];
        featureValues["assetGroupCode"] = element["assetGroupCode"];
        featureValues["assetTypeCode"] = element["assetTypeCode"];
        featureValues["objectId"] = element["objectId"];
        featureValues["globalId"] = element["globalId"];

        if (element["terminalId"] != null)
        {
          if (features.ContainsKey(featureKey))
          {
            features[featureKey]["terminalIds"] = features[featureKey].ContainsKey("terminalIds") ? features[featureKey]["terminalIds"] : new List<object>();
            ((List<object>)features[featureKey]["terminalIds"]).Add(element["terminalId"]);
            if (element["terminalName"] != null)
            {
              features[featureKey]["terminalNames"] = features[featureKey].ContainsKey("terminalNames") ? features[featureKey]["terminalNames"] : new List<object>();
              ((List<object>)features[featureKey]["terminalNames"]).Add(element["terminalName"]);
            }
            // Already processed the first instance of the feature
            continue;
          }
          else
          {
            featureValues["terminalIds"] = new List<object> { element["terminalId"] };
            if (element["terminalName"] != null)
            {
              featureValues["terminalNames"] = new List<object> { element["terminalName"] };
            }
          }
        }
        else if (features.ContainsKey(featureKey))
        {
          Dictionary<string, object> otherFeature = features[featureKey];
          if (element["positionFrom"] != null)
          {
            // Process multiple geometries, but reuse the attributes
            // Put this before the terminal check, so we allow internal edges
            featureValues = otherFeature;
          }
          else if (otherFeature.ContainsKey("terminalIds"))
          {
            // Don't add a duplicate feature
            continue;
          }
          else
          {
            Debug.WriteLine($"Duplicate feature: {featureKey}");
          }
        }

        if (element["networkSourceName"] != null)
        {
          featureValues["networkSourceName"] = element["networkSourceName"];
          featureValues["assetGroupName"] = element["assetGroupName"];
          featureValues["assetTypeName"] = element["assetTypeName"];
        }
        else
        {
          int nsid = Convert.ToInt32(element["networkSourceId"]);
          featureValues["networkSourceName"] = sourceMapping.ContainsKey(nsid) ? sourceMapping[nsid] : "System Junction";
        }

        // Only parse the fields, network attributes, and network descriptions
        JToken geometryElement = element["geometry"];
        if (geometryElement != null)
        {
          if (geometryElement["x"] != null)
          {
            // Create a point
            points[featureKey] = new List<double>
                    {
                        (double)geometryElement["x"],
                        (double)geometryElement["y"],
                        (double)geometryElement["z"]
                    };
          }
          else if (element["positionFrom"] != null)
          {
            if (complexGeometries)
            {
              // Append the line segment's geometry, along with the position along percent
              // We use this to stitch together all the line segments later
              List<dynamic> otherGeometries = lines.ContainsKey(featureKey) ? lines[featureKey] : new List<dynamic>();
              otherGeometries.Add(new List<dynamic> { (double)element["positionFrom"], geometryElement });
              lines[featureKey] = otherGeometries;
            }
            else
            {
              Tuple<JToken, JToken, JToken, JToken> fullKey = Tuple.Create(element["networkSourceId"], element["globalId"], element["positionFrom"], element["positionTo"]);
              lines[fullKey] = geometryElement;
            }
          }
        }
        JToken fieldValues = element["fieldValues"];
        if (fieldValues != null)
        {
          foreach (JToken field in fieldValues)
          {
            string fieldName = field["fieldName"].ToString();
            featureValues[fieldName] = field["value"];
            if (field["description"] != null)
            {
              string fieldNameDesc = $"{fieldName}_Desc";
              featureValues[fieldNameDesc] = field["description"];
            }
          }
        }
        JToken networkAttributes = element["networkAttributeValues"];
        if (networkAttributes != null)
        {
          foreach (JToken field in networkAttributes)
          {
            foreach (JProperty attribute in field)
            {
              featureValues[attribute.Name] = attribute.Value;
            }
          }
        }
        JToken networkAttributesDescriptions = element["networkAttributeDescriptions"];
        if (networkAttributesDescriptions != null)
        {
          foreach (JToken field in networkAttributesDescriptions)
          {
            foreach (JProperty attribute in field)
            {
              string attributeNameDesc = $"{attribute.Name}_Desc";
              featureValues[attributeNameDesc] = attribute.Value;
            }
          }
        }

        features[featureKey] = featureValues;
      }
      double duplicatePercent = (featuresElement.Count() - features.Count) / (double)featuresElement.Count();
      sb.AppendLine($"Unique feature elements #{features.Count:n0}  ({duplicatePercent:F2}%duplicate)");
      sb.AppendLine($"Unique points #{points.Count:n0} ");
      sb.AppendLine($"Unique lines #{lines.Count:n0}");

      return (features, points, lines, sb.ToString());
    }

    // Process connectivity
    private static (Dictionary<string, List<string>> connectivities, Dictionary<dynamic, dynamic> connectivitiesGeometry, string connectivitySummary) ProcessConnectivity(JToken connectivityElement, Dictionary<int, string> sourceMapping)
    {
      StringBuilder sb = new StringBuilder();
      // Initialize dictionaries for connectivity and connectivity_geometries
      Dictionary<string, List<string>> connectivity = new Dictionary<string, List<string>>();
      Dictionary<dynamic, dynamic> connectivityGeometries = new Dictionary<dynamic, dynamic>();

      sb.AppendLine($"Connectivity elements #{connectivityElement.Count():n0}");

      foreach (JToken element in connectivityElement)
      {
        string fromKey = (element["fromNetworkSourceId"], element["fromGlobalId"], element["fromTerminalId"]).ToString().Trim().Replace(" ", string.Empty);
        string toKey = (element["toNetworkSourceId"], element["toGlobalId"], element["toTerminalId"]).ToString().Trim().Replace(" ", string.Empty);
        string viaKey = (element["viaNetworkSourceId"], element["viaGlobalId"], element["viaPositionFrom"], element["viaPositionTo"]).ToString().Trim().Replace(" ", string.Empty);

        // Store the connectivity
        List<string> fromConnections = connectivity.ContainsKey(fromKey) ? connectivity[fromKey] : new List<string>();
        if (!fromConnections.Contains(viaKey))
        {
          fromConnections.Add(viaKey);
        }


        List<string> toConnections = connectivity.ContainsKey(toKey) ? connectivity[toKey] : new List<string>();
        if (!toConnections.Contains(viaKey))
        {
          toConnections.Add(viaKey);
        }

        // If the via connection is a connectivity association we need to add edges to both sides
        connectivity[fromKey] = fromConnections;
        //connectivity[viaKey] = (fromKey, toKey);
        connectivity[toKey] = toConnections;

        // Store the geometries
        JToken fromGeometry = element["fromGeometry"];
        if (fromGeometry != null)
        {
          connectivityGeometries[fromKey] = fromGeometry;
        }

        JToken toGeometry = element["toGeometry"];
        if (toGeometry != null)
        {
          connectivityGeometries[toKey] = toGeometry;
        }

        JToken viaGeometry = element["viaGeometry"];
        if (viaGeometry != null)
        {
          connectivityGeometries[viaKey] = viaGeometry;
        }
      }

      sb.AppendLine($"Unique connections #{connectivity.Count:n0}");
      sb.AppendLine($"Connectivity geometries #{connectivityGeometries.Count:n0}");

      return (connectivity, connectivityGeometries, sb.ToString());
    }

    // Process associations
    private static (Dictionary<dynamic, dynamic> associations, string associationSummary) ProcessAssociations(JToken associationsElement, Dictionary<int, string> sourceMapping)
    {
      StringBuilder sb = new StringBuilder();
      Dictionary<dynamic, dynamic> associations = new Dictionary<dynamic, dynamic>();

      sb.AppendLine($"Association elements #{associationsElement.Count():n0}");
      HashSet<Tuple<object, object, object, object>> uniqueConnections = new HashSet<Tuple<object, object, object, object>>();
      foreach (JToken element in associationsElement)
      {
        Tuple<object, object, object, object> tupleKey = Tuple.Create((object)element["fromNetworkSourceId"], (object)element["fromGlobalId"],
                                      (object)element["toNetworkSourceId"], (object)element["toGlobalId"]);
        uniqueConnections.Add(tupleKey);
      }
      sb.AppendLine($"Unique association elements #{uniqueConnections.Count:n0}");

      foreach (JToken element in associationsElement)
      {
        // Note, associations don't have an object id so we reference them by global id
        (JToken fromNetworkSourceID, JToken fromGlobalID) fromKey = (element["fromNetworkSourceId"], element["fromGlobalId"]);
        (JToken toNetworkSourceID, JToken toGlobalID) toKey = (element["toNetworkSourceId"], element["toGlobalId"]);
        JToken associationType = element["associationType"];

        List<dynamic> fromAssociations = associations.ContainsKey(fromKey) ? associations[fromKey] : new List<dynamic>();
        fromAssociations.Add(new List<dynamic> { associationType, toKey });
        associations[fromKey] = fromAssociations;

        List<dynamic> toAssociations = associations.ContainsKey(toKey) ? associations[toKey] : new List<dynamic>();
        toAssociations.Add(new List<dynamic> { associationType, toKey });
        associations[toKey] = toAssociations;
      }
      sb.AppendLine($"Exploded from/to associations #{associations.Count:n0}");

      return (associations, sb.ToString());
    }


  }
}
