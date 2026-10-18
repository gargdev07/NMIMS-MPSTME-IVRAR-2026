using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace S021.Analytics
{
    [Serializable]
    public sealed class S021SessionMetrics
    {
        public string sessionId;
        public string participantId = "DEMO";
        public string condition = "EDITOR_DEMO";
        public string destination;
        public string targetFloor;
        public string startTime;
        public string endTime;
        public float transitDurationSeconds;
        public float totalDistanceMeters;
        public float expectedDistanceMeters;
        public int deviationCount;
        public int wrongTurnCount;
        public int waypointsReached;
        public int floorTransitions;
        public float maximumDeviationMeters;
        public bool destinationCompleted;
        public string completionStatus = "NotStarted";
        public float susScore;
        public float nasaTlxScore;
        public bool hasSurveyScores;
    }

    public sealed class S021NavigationAnalytics : MonoBehaviour
    {
        [SerializeField] private MonoBehaviour navigationEventsComponent;
        [SerializeField] private MonoBehaviour routeProviderComponent;
        [SerializeField] private MonoBehaviour poseProviderComponent;
        [SerializeField, Min(0.01f)] private float deviationThresholdMeters = 4f;

        private S014.MobileAR.IS014NavigationEvents navigationEvents;
        private S014.MobileAR.IS014RouteProvider routeProvider;
        private S014.MobileAR.IS014PoseProvider poseProvider;
        private readonly List<S014.MobileAR.S014Waypoint> route = new List<S014.MobileAR.S014Waypoint>();
        private bool subscribed;
        private bool active;
        private bool deviating;
        private int segmentIndex;
        private Vector3 lastPosition;
        private bool hasLastPosition;
        private float elapsed;

        public S021SessionMetrics Metrics { get; private set; }
        public float CurrentDeviationMeters { get; private set; }
        public float DeviationThresholdMeters => deviationThresholdMeters;
        public bool HasCompletedSession => Metrics != null &&
                                          Metrics.completionStatus == "Completed" &&
                                          !string.IsNullOrWhiteSpace(Metrics.sessionId);

        private void Awake()
        {
            BindDependencies();
            Metrics = NewMetrics();
            ReloadRoute();
        }

        private void OnEnable()
        {
            if (subscribed) Unsubscribe();
            BindDependencies();
            Subscribe();
        }

        private void OnDisable()
        {
            Unsubscribe();
        }

        private void BindDependencies()
        {
            var currentEvents = navigationEventsComponent as S014.MobileAR.IS014NavigationEvents;
            if (subscribed && !ReferenceEquals(navigationEvents, currentEvents)) Unsubscribe();
            navigationEvents = currentEvents;
            routeProvider = routeProviderComponent as S014.MobileAR.IS014RouteProvider;
            poseProvider = poseProviderComponent as S014.MobileAR.IS014PoseProvider;
        }

        private void Update()
        {
            if (!active || poseProvider == null) return;
            elapsed += Time.deltaTime;
            Vector3 position;
            Quaternion rotation;
            if (!poseProvider.TryGetPose(out position, out rotation)) return;
            if (hasLastPosition) Metrics.totalDistanceMeters += Vector3.Distance(position, lastPosition);
            lastPosition = position;
            hasLastPosition = true;
            CurrentDeviationMeters = CalculateRouteDeviation(position);
            if (CurrentDeviationMeters > deviationThresholdMeters && !deviating)
            {
                deviating = true;
                Metrics.deviationCount++;
                Metrics.wrongTurnCount++;
            }
            else if (CurrentDeviationMeters <= deviationThresholdMeters)
            {
                deviating = false;
            }
            Metrics.maximumDeviationMeters = Mathf.Max(Metrics.maximumDeviationMeters, CurrentDeviationMeters);
            Metrics.transitDurationSeconds = elapsed;
        }

        public void ConfigureSession(string sessionId, string destination, string targetFloor)
        {
            Metrics = NewMetrics();
            Metrics.sessionId = sessionId;
            Metrics.destination = destination;
            Metrics.targetFloor = targetFloor;
            ReloadRoute();
        }

        public void ReloadRoute()
        {
            route.Clear();
            if (routeProvider == null) return;
            var provided = routeProvider.GetRoute();
            if (provided == null) return;
            foreach (var waypoint in provided)
            {
                if (waypoint != null) route.Add(waypoint.Copy(route.Count));
            }
            segmentIndex = 0;
            Metrics.expectedDistanceMeters = CalculateExpectedDistance();
        }

        public void SetSurveyScores(float sus, float nasaTlx)
        {
            Metrics.susScore = sus;
            Metrics.nasaTlxScore = nasaTlx;
            Metrics.hasSurveyScores = true;
        }

        public string ExportCompletedCsv()
        {
            var directory = Path.Combine(Application.persistentDataPath, "S021");
            Directory.CreateDirectory(directory);
            var path = Path.Combine(directory, "navigation_sessions.csv");
            if (!File.Exists(path) || new FileInfo(path).Length == 0)
                File.WriteAllText(path, CsvHeader + Environment.NewLine, new UTF8Encoding(false));

            string existingHeader;
            using (var reader = new StreamReader(path)) existingHeader = reader.ReadLine();
            if (!string.Equals(existingHeader, CsvHeader, StringComparison.Ordinal))
                MigrateCsvSchema(path, existingHeader);

            if (HasCompletedSession)
            {
                var sessionPrefix = Escape(Metrics.sessionId) + ",";
                foreach (var line in File.ReadLines(path))
                {
                    if (line.StartsWith(sessionPrefix, StringComparison.Ordinal))
                        throw new InvalidOperationException("This session is already present in the CSV; the file was not changed.");
                }

                File.AppendAllText(path, Serialize(Metrics) + Environment.NewLine, new UTF8Encoding(false));
            }
            return path;
        }

        public float CalculateRouteDeviation(Vector3 position)
        {
            if (route.Count < 2) return 0f;
            var startIndex = Mathf.Clamp(segmentIndex, 0, route.Count - 2);
            var start = route[startIndex].worldPosition;
            var end = route[startIndex + 1].worldPosition;
            var segment = end - start;
            var lengthSquared = segment.sqrMagnitude;
            var projected = lengthSquared <= Mathf.Epsilon
                ? start
                : start + segment * Mathf.Clamp01(Vector3.Dot(position - start, segment) / lengthSquared);
            return Vector3.Distance(position, projected);
        }

        private void Subscribe()
        {
            BindDependencies();
            if (subscribed || navigationEvents == null) return;
            navigationEvents.OnNavigationStarted += HandleStarted;
            navigationEvents.OnNavigationReset += HandleReset;
            navigationEvents.OnWaypointReached += HandleWaypoint;
            navigationEvents.OnFloorTransition += HandleFloor;
            navigationEvents.OnDestinationReached += HandleDestination;
            subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!subscribed) return;
            if (navigationEvents != null)
            {
                navigationEvents.OnNavigationStarted -= HandleStarted;
                navigationEvents.OnNavigationReset -= HandleReset;
                navigationEvents.OnWaypointReached -= HandleWaypoint;
                navigationEvents.OnFloorTransition -= HandleFloor;
                navigationEvents.OnDestinationReached -= HandleDestination;
            }
            subscribed = false;
        }

        private void HandleStarted()
        {
            active = true;
            elapsed = 0f;
            hasLastPosition = false;
            if (poseProvider != null)
            {
                Vector3 position;
                Quaternion rotation;
                if (poseProvider.TryGetPose(out position, out rotation))
                {
                    lastPosition = position;
                    hasLastPosition = true;
                }
            }
            deviating = false;
            segmentIndex = 0;
            Metrics.startTime = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
            Metrics.completionStatus = "Active";
        }

        private void HandleReset()
        {
            active = false;
            elapsed = 0f;
            hasLastPosition = false;
            deviating = false;
            Metrics = NewMetrics();
            ReloadRoute();
        }

        private void HandleWaypoint(S014.MobileAR.S014Waypoint waypoint)
        {
            if (waypoint == null) return;
            Metrics.waypointsReached++;
            segmentIndex = Mathf.Clamp(waypoint.index, 0, Mathf.Max(0, route.Count - 2));
        }

        private void HandleFloor(S014.MobileAR.S014Waypoint waypoint)
        {
            Metrics.floorTransitions++;
        }

        private void HandleDestination()
        {
            if (poseProvider != null)
            {
                Vector3 position;
                Quaternion rotation;
                if (poseProvider.TryGetPose(out position, out rotation))
                {
                    if (hasLastPosition) Metrics.totalDistanceMeters += Vector3.Distance(position, lastPosition);
                    lastPosition = position;
                    hasLastPosition = true;
                }
            }
            active = false;
            Metrics.transitDurationSeconds = elapsed;
            Metrics.endTime = DateTime.Now.ToString("o", CultureInfo.InvariantCulture);
            Metrics.destinationCompleted = true;
            Metrics.completionStatus = "Completed";
        }

        private S021SessionMetrics NewMetrics()
        {
            return new S021SessionMetrics { sessionId = Guid.NewGuid().ToString("N").Substring(0, 8).ToUpperInvariant() };
        }

        private float CalculateExpectedDistance()
        {
            var total = 0f;
            for (var i = 1; i < route.Count; i++)
                total += Vector3.Distance(route[i - 1].worldPosition, route[i].worldPosition);
            return total;
        }

        private const string CsvHeader = "session_id,participant_id,condition,destination,target_floor,start_time,end_time,transit_duration_seconds,total_distance_meters,expected_distance_meters,deviation_count,wrong_turn_count,maximum_deviation_meters,waypoints_reached,floor_transitions,destination_completed,completion_status,sus_score,nasa_tlx_score";
        private const string LegacyCsvHeader = "session_id,participant_id,condition,destination,target_floor,start_time,end_time,transit_duration_seconds,total_distance_meters,expected_distance_meters,deviation_count,wrong_turn_count,waypoints_reached,floor_transitions,sus_score,nasa_tlx_score";

        private static void MigrateCsvSchema(string path, string header)
        {
            if (!string.Equals(header, LegacyCsvHeader, StringComparison.Ordinal))
                throw new InvalidDataException("The existing CSV header does not match a supported S021 schema; the file was not changed.");

            var records = ParseCsvRecords(File.ReadAllText(path, Encoding.UTF8));
            if (records.Count == 0 || records[0].Count == 0)
                throw new InvalidDataException("The existing CSV has no readable header; the file was not changed.");
            if (records[0][0].Length > 0 && records[0][0][0] == '\uFEFF')
                records[0][0] = records[0][0].Substring(1);
            if (string.Join(",", records[0]) != LegacyCsvHeader)
                throw new InvalidDataException("The existing CSV header changed during migration; the file was not changed.");

            var migrated = new StringBuilder();
            migrated.Append(CsvHeader).Append(Environment.NewLine);
            for (var index = 1; index < records.Count; index++)
            {
                var row = records[index];
                if (row.Count == 1 && string.IsNullOrEmpty(row[0])) continue;
                if (row.Count == 19)
                {
                    for (var column = 0; column < row.Count; column++)
                    {
                        if (column > 0) migrated.Append(',');
                        migrated.Append(Escape(row[column]));
                    }
                    migrated.Append(Environment.NewLine);
                    continue;
                }
                if (row.Count != 16)
                    throw new InvalidDataException("The legacy CSV contains a row with an unexpected column count; the file was not changed.");

                var current = new List<string>(19);
                for (var column = 0; column < 12; column++) current.Add(row[column]);
                current.Add("0"); // Legacy exports did not record maximum deviation.
                current.Add(row[12]);
                current.Add(row[13]);
                current.Add("true"); // Rows in the legacy completed-session export were written on arrival.
                current.Add("Completed");
                current.Add(row[14]);
                current.Add(row[15]);
                for (var column = 0; column < current.Count; column++)
                {
                    if (column > 0) migrated.Append(',');
                    migrated.Append(Escape(current[column]));
                }
                migrated.Append(Environment.NewLine);
            }

            var tempPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
            var backupPath = path + ".bak-" + DateTime.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
            try
            {
                File.WriteAllText(tempPath, migrated.ToString(), new UTF8Encoding(false));
                File.Replace(tempPath, path, backupPath);
            }
            finally
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
        }

        private static List<List<string>> ParseCsvRecords(string content)
        {
            var records = new List<List<string>>();
            var row = new List<string>();
            var field = new StringBuilder();
            var quoted = false;
            for (var index = 0; index < content.Length; index++)
            {
                var character = content[index];
                if (quoted)
                {
                    if (character == '"')
                    {
                        if (index + 1 < content.Length && content[index + 1] == '"')
                        {
                            field.Append('"');
                            index++;
                        }
                        else quoted = false;
                    }
                    else field.Append(character);
                    continue;
                }

                if (character == '"')
                {
                    if (field.Length != 0)
                        throw new InvalidDataException("The legacy CSV has malformed quoting; the file was not changed.");
                    quoted = true;
                }
                else if (character == ',')
                {
                    row.Add(field.ToString());
                    field.Clear();
                }
                else if (character == '\r' || character == '\n')
                {
                    row.Add(field.ToString());
                    field.Clear();
                    records.Add(row);
                    row = new List<string>();
                    if (character == '\r' && index + 1 < content.Length && content[index + 1] == '\n') index++;
                }
                else field.Append(character);
            }

            if (quoted) throw new InvalidDataException("The legacy CSV has an unterminated quoted field; the file was not changed.");
            if (field.Length > 0 || row.Count > 0)
            {
                row.Add(field.ToString());
                records.Add(row);
            }
            return records;
        }

        private static string Serialize(S021SessionMetrics m)
        {
            return string.Join(",", Escape(m.sessionId), Escape(m.participantId), Escape(m.condition), Escape(m.destination), Escape(m.targetFloor), Escape(m.startTime), Escape(m.endTime),
                m.transitDurationSeconds.ToString("F1", CultureInfo.InvariantCulture), m.totalDistanceMeters.ToString("F1", CultureInfo.InvariantCulture),
                m.expectedDistanceMeters.ToString("F1", CultureInfo.InvariantCulture), m.deviationCount, m.wrongTurnCount,
                m.maximumDeviationMeters.ToString("F1", CultureInfo.InvariantCulture), m.waypointsReached,
                m.floorTransitions, m.destinationCompleted, Escape(m.completionStatus),
                m.hasSurveyScores ? m.susScore.ToString("F1", CultureInfo.InvariantCulture) : string.Empty,
                m.hasSurveyScores ? m.nasaTlxScore.ToString("F1", CultureInfo.InvariantCulture) : string.Empty);
        }

        private static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value)) return string.Empty;
            var escaped = value.Replace("\"", "\"\"");
            return escaped.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0 ? "\"" + escaped + "\"" : escaped;
        }
    }
}
