using UnityEngine;
using UnityEngine.AI;
using System.Collections.Generic;

[RequireComponent(typeof(NavMeshAgent))]
public class PathTester : MonoBehaviour
{
    public GameObject waypointPrefab; 
    private NavMeshAgent agent;
    private NavMeshPath path;
    private List<GameObject> waypoints = new List<GameObject>(); 

    void Start()
    {
        agent = GetComponent<NavMeshAgent>();
        path = new NavMeshPath();
    }

    void Update()
    {
        if (Input.GetKeyDown(KeyCode.N)) 
        {
            DestroyWaypoints(); 
            Vector3 targetPoint = GetCameraTargetPoint();
            CalculatePath(targetPoint);
        }

    
        if (agent.hasPath)
        {
            MoveAgentAlongPath();
        }
    }

   
    Vector3 GetCameraTargetPoint()
    {
        Ray ray = Camera.main.ScreenPointToRay(Input.mousePosition); 
        RaycastHit hit;
         
        if (Physics.Raycast(ray, out hit))
        {
          
            if (NavMesh.SamplePosition(hit.point, out NavMeshHit navHit, 1.0f, NavMesh.AllAreas))
            {
                return navHit.position; 
            }
        } 

       
        Debug.LogWarning("No valid NavMesh point found.");
        return transform.position; 
    }

    void CalculatePath(Vector3 targetPosition)
    {
        bool pathFound = agent.CalculatePath(targetPosition, path);
 

        if (pathFound)
        {
            Debug.Log($"Path status: {path.status}");

            if (path.status == NavMeshPathStatus.PathComplete)
            {
                Debug.Log("Path is complete.");
                PrintPathDetails(); 
                DrawPath();
                MoveToTarget(targetPosition); 
            }
            else if (path.status == NavMeshPathStatus.PathPartial)
            {
                Debug.LogWarning("Path is partial.");
                PrintPathDetails();  
            }
            else
            {
                Debug.LogError("No valid path.");
            }
        }
        else
        {
            Debug.LogError("Failed to calculate path.");
        }
    }

    void MoveToTarget(Vector3 targetPosition)
    {
        agent.SetDestination(targetPosition);
        MarkWaypoints(); 
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

    void DrawPath()
    {
        if (path.corners.Length < 2)
            return;

        for (int i = 0; i < path.corners.Length - 1; i++)
        {
            Debug.DrawLine(path.corners[i], path.corners[i + 1], Color.red, 2f); 
        }
    }

    void MarkWaypoints()
    {
        foreach (Vector3 corner in path.corners)
        {
            GameObject waypoint = Instantiate(waypointPrefab, corner, Quaternion.identity); 
            waypoints.Add(waypoint); 
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

    void MoveAgentAlongPath()
    {
        if (agent.remainingDistance < 0.1f)
        {
            agent.ResetPath(); 
        }
    }
}
