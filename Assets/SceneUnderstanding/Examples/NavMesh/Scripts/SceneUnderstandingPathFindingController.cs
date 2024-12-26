// Copyright (c) Microsoft Corporation. All rights reserved.
/*
 *功能：
 *动态导航
 */
namespace Microsoft.MixedReality.SceneUnderstanding.Samples.Unity
{
    using UnityEngine;
    using UnityEngine.AI;
    using System.Collections.Generic;
    using System.Text;
    using TMPro;
    using System.Collections;

    /// <summary>
    /// This Script defines the logic a NavMesh agent in a unity navmesh
    /// </summary>
    public class SceneUnderstandingPathFindingController : MonoBehaviour
    {
        [SerializeField]
        private TMP_Text pathDisplayText;
        //Raycast used to determine where the nav mesh agent will move
        private RaycastHit raycastHit;

        //Reference to the NavMeshAgent
        private GameObject gbjNavMeshAgent;
        
        //11.19 
        public GameObject waypointPrefab;
        private NavMeshPath path;
        private NavMeshAgent agent;
        private List<GameObject> waypoints = new List<GameObject>();
        //11.19

        private const float ANGLE_THRESHOLD = 15f;
        private const float METERS_PER_STEP = 0.5f;//步幅
        private const float WAYPOINT_REACH_THRESHOLD = 0.25f;//路径点附近判定
        private const float DIRECTION_CHECK_THRESHOLD = 10f; // 用户转向的容差范围

        [SerializeField]
        private UnityWsTts unityWsTts;
        //动态播报相关
        private int currentPathSegment = 0;
        private List<PathSegmentInfo> pathSegments = new List<PathSegmentInfo>();
        private bool isNavigating = false;
        //转向相关
        private bool waitingForTurn = false;
        private float targetAngle = 0f;

        //检测路径偏离相关
        private const float OFF_TRACK_CHECK_INTERVAL = 2f; // 检查间隔时间
        private float lastOffTrackCheckTime = 0f; // 上次检查时间
        private bool hasWarnedOffTrack = false; // 是否已经发出过偏离警告
        private const float MAX_PATH_DEVIATION = 0.5f; // 允许偏离路径的最大距离（米）

        // 路径段信息类
        private class PathSegmentInfo
        {
            public float turnAngle;
            public float distance;
            public bool isStraight;
            public Vector3 startPoint;
            public Vector3 endPoint;
            public string navigationMessage;
        }

        private void Start()
        {
            // 确保在开始时有Text组件引用
            if (pathDisplayText == null)
            {
                Debug.LogError("请在Inspector中设置Path Display Text引用!");
            }
        }

        private void Update()
        {
            if (!isNavigating || currentPathSegment >= pathSegments.Count) return;

            if (waitingForTurn)
            {
                CheckUserOrientation();
            }
            else
            {
                CheckWaypointReached();
            }
        }

        private void CheckUserOrientation()
        {
            // 获取用户当前朝向（水平面）
            Vector3 userForward = Camera.main.transform.forward;
            userForward.y = 0;
            userForward.Normalize();

            // 获取期望的朝向方向（从当前路径段的起点指向终点）
            PathSegmentInfo currentSegment = pathSegments[currentPathSegment];
            Vector3 desiredDirection = (currentSegment.endPoint - currentSegment.startPoint).normalized;
            desiredDirection.y = 0;

            // 计算用户朝向与期望方向的夹角
            float angleDifference = Vector3.Angle(userForward, desiredDirection);

            // 检查是否在允许的误差范围内
            if (angleDifference <= DIRECTION_CHECK_THRESHOLD)
            {
                waitingForTurn = false;
                // 播报距离信息
                int steps = Mathf.Max(1, Mathf.FloorToInt(currentSegment.distance / METERS_PER_STEP));
                string distanceMessage = $"直行{steps}步";
                UpdatePathDisplay(distanceMessage);
                UnityWebTTS();
            }
        }

        private void CheckWaypointReached()
        {
            if (currentPathSegment >= pathSegments.Count) return;

            PathSegmentInfo currentSegment = pathSegments[currentPathSegment];
            Vector3 currentWaypointPos = currentSegment.endPoint;
            Vector3 userPos = Camera.main.transform.position;
            userPos.y = currentWaypointPos.y;

            float distance = Vector3.Distance(userPos, currentWaypointPos);

            // 检查是否偏离路径
            CheckIfOffTrack(currentSegment, userPos);

            if (distance < WAYPOINT_REACH_THRESHOLD)
            {
                hasWarnedOffTrack = false; // 重置警告状态
                currentPathSegment++;
                if (currentPathSegment < pathSegments.Count)
                {
                    PlayNextSegment();
                }
                else
                {
                    PlayDestinationReached();
                }
            }
        }

        // 检查是否偏离路径
        private void CheckIfOffTrack(PathSegmentInfo currentSegment, Vector3 userPos)
        {
            // 如果距离上次检查时间不足，直接返回
            if (Time.time - lastOffTrackCheckTime < OFF_TRACK_CHECK_INTERVAL)
            {
                return;
            }

            lastOffTrackCheckTime = Time.time;

            // 如果已经在等待转向或已经发出警告，不进行偏离检查
            if (waitingForTurn || hasWarnedOffTrack)
            {
                return;
            }

            float distanceToPath = CalculateDistanceToPath(userPos, currentSegment.startPoint, currentSegment.endPoint);

            if (distanceToPath > MAX_PATH_DEVIATION)
            {
                // 获取当前的目标点（最终目的地）
                Vector3 finalDestination = raycastHit.point;

                // 使用协程处理偏离路径的情况
                StartCoroutine(HandleOffTrackNavigation(finalDestination));
            }
        }

        private IEnumerator HandleOffTrackNavigation(Vector3 finalDestination)
        {
            // 设置状态防止重复触发
            hasWarnedOffTrack = true;

            // 播放偏离提示
            string message = "检测到偏离路线,已重新规划路径";
            UpdatePathDisplay(message);
            UnityWebTTS();

            // 等待6秒给用户反应时间
            yield return new WaitForSeconds(6f);

            // 重新进行导航
            DestroyWaypoints();
            gbjNavMeshAgent = GameObject.FindGameObjectWithTag("NavAgent");

            if (gbjNavMeshAgent != null)
            {
                // 重新导航
                MoveNav(gbjNavMeshAgent, finalDestination);

                // 重新计算并播报路径
                DescribePath();
                currentPathSegment = 0;
                if (pathSegments.Count > 0)
                {
                    PlayNextSegment();
                }

                // 等待10秒后才允许再次检测偏离
                yield return new WaitForSeconds(10f);
                hasWarnedOffTrack = false;
            }
            else
            {
                Debug.LogError("无法找到NavMeshAgent对象");
                // 如果找不到导航对象，也需要重置状态
                hasWarnedOffTrack = false;
            }
        }

        // 计算点到线段的最短距离
        private float CalculateDistanceToPath(Vector3 point, Vector3 lineStart, Vector3 lineEnd)
        {
            Vector3 line = lineEnd - lineStart;
            float lineLength = line.magnitude;
            Vector3 lineDirection = line.normalized;

            Vector3 pointVector = point - lineStart;
            float projection = Vector3.Dot(pointVector, lineDirection);

            // 如果投影点在线段外
            if (projection <= 0)
                return Vector3.Distance(point, lineStart);
            if (projection >= lineLength)
                return Vector3.Distance(point, lineEnd);

            // 计算投影点
            Vector3 projectionPoint = lineStart + lineDirection * projection;
            return Vector3.Distance(point, projectionPoint);
        }

        // 播放特定段的导航指示
        private void PlayNextSegment()
        {
            if (currentPathSegment >= pathSegments.Count) return;

            PathSegmentInfo segment = pathSegments[currentPathSegment];

            if (segment.isStraight)
            {
                // 直接播报直行距离
                int steps = Mathf.Max(1, Mathf.FloorToInt(segment.distance / METERS_PER_STEP));
                string message = $"直走，直行{steps}步";
                UpdatePathDisplay(message);
                UnityWebTTS();
            }
            else
            {
                // 先播报转向指示
                string turnDirection = segment.turnAngle > 0 ? "右" : "左";
                string message = $"向{turnDirection}转{Mathf.Abs(segment.turnAngle):F1}度";
                UpdatePathDisplay(message);
                UnityWebTTS();

                // 设置等待转向状态
                waitingForTurn = true;
                targetAngle = segment.turnAngle;
            }
        }

        // 播放到达目的地提示
        private void PlayDestinationReached()
        {
            string destinationText = "已到达目的地";
            UpdatePathDisplay(destinationText);

            if (pathDisplayText != null && !string.IsNullOrEmpty(pathDisplayText.text))
            {
                if (unityWsTts != null)
                {
                    unityWsTts.PlayTextFromTMP();
                }
            }
            isNavigating = false;
        }

        // The agent will move wherever the main camera is gazing at.
        public void MoveAgent()
        {
            if (Physics.Raycast(Camera.main.transform.position, Camera.main.transform.TransformDirection(Vector3.forward), out raycastHit, Mathf.Infinity))
            {
                // 重置偏离检测状态
                hasWarnedOffTrack = false;
                DestroyWaypoints();
                gbjNavMeshAgent = GameObject.FindGameObjectWithTag("NavAgent");
                path = new NavMeshPath();
                agent = gbjNavMeshAgent.GetComponent<NavMeshAgent>();
                if (agent == null)
                {
                    Debug.LogError("NavMeshAgent component not found on the NavMeshAgent GameObject.");
                    return;
                }
                MoveNav(gbjNavMeshAgent, raycastHit.point);
                PrintPathDetails();
                DescribePath();
            }
        }

        void DestroyWaypoints()
        {
            foreach (GameObject waypoint in waypoints)
            {
                Destroy(waypoint);
            }
            waypoints.Clear();
        }

        void MoveNav(GameObject NavMeshAgent, Vector3 targetPosition)
        {
            Camera mainCamera = Camera.main;
            if (mainCamera == null)
            {
                Debug.LogError("Main camera not found.");
                return;
            }

            // 获取用户实际朝向（只考虑水平面）
            Vector3 userForward = mainCamera.transform.forward;
            userForward.y = 0;
            userForward.Normalize();

            // 先停止当前的导航
            agent.isStopped = true;
            agent.ResetPath();

            // 更新 NavMeshAgent 的起始位置为摄像机正下方
            Vector3 newPosition = new Vector3(mainCamera.transform.position.x, 0.0f, mainCamera.transform.position.z);
            NavMeshAgent.transform.position = newPosition;

            // 确保新位置在NavMesh上
            NavMeshHit hit;
            if (NavMesh.SamplePosition(newPosition, out hit, 2.0f, NavMesh.AllAreas))
            {
                NavMeshAgent.transform.position = hit.position;
            }

            // 使用实际的用户朝向来设置NavAgent的朝向
            if (userForward != Vector3.zero)
            {
                NavMeshAgent.transform.rotation = Quaternion.LookRotation(userForward);
                // 确保 NavMeshAgent 的朝向完全对齐用户朝向
                agent.updateRotation = false; // 暂时禁用 NavMeshAgent 的自动旋转
            }

            // 重新启用导航
            agent.isStopped = false;

            // 获取射线碰撞的法线方向
            Vector3 wallNormal = raycastHit.normal;
            float offsetDistance = 1.0f;  //与墙的碰撞距离
            Vector3 adjustedTargetPosition = targetPosition + (wallNormal * offsetDistance);

            // 确保目标点在 NavMesh 上
            NavMeshHit navHit;
            if (NavMesh.SamplePosition(adjustedTargetPosition, out navHit, 2.0f, NavMesh.AllAreas))
            {
                bool pathFound = agent.CalculatePath(navHit.position, path);

                if (pathFound && path.status == NavMeshPathStatus.PathComplete)
                {
                    agent.SetDestination(navHit.position);
                    MarkWaypoints();
                }
            }

            // 重新启用 NavMeshAgent 的自动旋转（如果需要）
            agent.updateRotation = true;
        }

        void PrintPathDetails()
        {
            Debug.Log($"Number of corners (waypoints): {path.corners.Length}");

            for (int i = 0; i < path.corners.Length; i++)
            {
                Vector3 corner = path.corners[i];
                Debug.Log($"Corner {i + 1}: Position = {corner}, Height = {corner.y}");
            }

            if (path.corners.Length > 1)
            {
                float totalDistance = CalculateTotalPathDistance();
                Debug.Log($"Total path distance: {totalDistance} units");
            }
            else
            {
                Debug.Log("Path does not have enough corners to calculate a total distance.");
            }
        }

        float CalculateTotalPathDistance()
        {
            float distance = 0f;

            for (int i = 0; i < path.corners.Length - 1; i++)
            {
                distance += Vector3.Distance(path.corners[i], path.corners[i + 1]);
            }

            return distance;
        }

        void MarkWaypoints()
        {
            foreach (Vector3 corner in path.corners)
            {
                GameObject waypoint = Instantiate(waypointPrefab, corner, Quaternion.identity);
                waypoints.Add(waypoint);
            }
        }

        private void DescribePath()
        {
            if (path.corners.Length < 2)
            {
                Debug.Log("导航点不足，无法描述路径。");
                return;
            }

            // 重置导航状态
            currentPathSegment = 0;
            pathSegments.Clear();
            isNavigating = true;
            waitingForTurn = false;

            // 使用相机的实际朝向，而不是 NavMeshAgent 的朝向
            Vector3 initialForward = Camera.main.transform.forward;
            initialForward.y = 0; // 只考虑水平面方向
            initialForward.Normalize(); // 确保是单位向量

            for (int i = 0; i < path.corners.Length - 1; i++)
            {
                Vector3 currentPoint = path.corners[i];
                Vector3 nextPoint = path.corners[i + 1];

                Vector3 pathDirection = nextPoint - currentPoint;
                pathDirection.y = 0;

                float distance = pathDirection.magnitude;
                float angle = Vector3.SignedAngle(initialForward, pathDirection.normalized, Vector3.up);

                PathSegmentInfo segment = new PathSegmentInfo
                {
                    turnAngle = angle,
                    distance = distance,
                    isStraight = Mathf.Abs(angle) <= ANGLE_THRESHOLD,
                    startPoint = currentPoint,
                    endPoint = nextPoint
                };

                pathSegments.Add(segment);
                initialForward = pathDirection.normalized; // 更新参考方向为当前段的方向
            }

            // 开始第一段导航
            if (pathSegments.Count > 0)
            {
                PlayNextSegment();
            }
        }

        private string GetDirectionFromAngle(float angle)
        {
            // 将角度范围限制在 -180 到 180 度之间
            if (angle > 180f) angle -= 360f;
            if (angle < -180f) angle += 360f;
            if (angle > 90f) angle -= 180f;
            if (angle < -90f) angle += 180f;

            // 使用设定的阈值判断方向
            if (Mathf.Abs(angle) <= ANGLE_THRESHOLD)
            {
                return "直走";
                //return "up";
            }
            else if (angle > ANGLE_THRESHOLD)
            {
                return $"向右转{Mathf.Abs(angle):F1}度";
            }
            else
            {
                return $"向左转{Mathf.Abs(angle):F1}度";
            }
        }

        private void UpdatePathDisplay(string text)
        {
            if (pathDisplayText != null)
            {
                pathDisplayText.text = text;
            }
            else
            {
                Debug.LogWarning("Path Display Text组件未设置!");
            }
            // 保留Debug.Log以便在控制台也能看到输出
            Debug.Log(text);
        }

        void UnityWebTTS()
        {
            if (unityWsTts != null)
            {
                unityWsTts.PlayTextFromTMP();
            }
            else
            {
                Debug.LogError("UnityWsTts reference is not set.");
            }
        }

    }
}
