// Copyright (c) Microsoft Corporation. All rights reserved.
namespace Microsoft.MixedReality.SceneUnderstanding.Samples.Unity
{
    using UnityEngine;
    using UnityEngine.AI;
    using TMPro;

    enum AreaType
    {
        Walkable,
        NotWalkable
    }

    /// <summary>
    /// This Script will generate a NavMesh in a Scene Understanding Scene already generated inside unity
    /// using the unity built in NavMesh engine
    /// </summary>
    public class SceneUnderstandingNavMeshGenerator : MonoBehaviour
    {
        // Nav Mesh Surface is a Unity Built in Type, from 
        // the unity NavMesh Assets
        public NavMeshSurface navMeshSurf;
        public GameObject sceneRoot;
        public GameObject navMeshAgentRef;
        private GameObject navMeshAgentInstance;
        [SerializeField]
        private UnityWsTts unityWsTts;
        [SerializeField]
        private TMP_Text initMessageText;

        // This function runs as a callback for the OnLoadFinished event
        // In the SceneUnderstandingManager Component
        public void BakeMesh()
        {
            UpdateNavMeshSettingsForObjsUnderRoot();
            navMeshSurf.BuildNavMesh();
            CreateNavMeshAgent();
        }

        void CreateNavMeshAgent()
        {
            if(navMeshAgentRef == null)
            {
                return;
            }

            Camera mainCamera = Camera.main;
            if (mainCamera == null)
            {
                Debug.LogError("Main camera not found. Please ensure there is a main camera in the scene with the tag 'MainCamera'.");
                return;
            }

            // 检查 navMeshAgentInstance 是否已经存在
            if (navMeshAgentInstance == null)
            {
                // 获取主摄像机的位置
                Vector3 cameraPosition = mainCamera.transform.position;

                // 创建一个新的位置，X 和 Z 轴与摄像机相同，Y 轴设为 0
                Vector3 spawnPosition = new Vector3(cameraPosition.x, 0.0f, cameraPosition.z);

                // 在新的位置和主摄像机的旋转下实例化 navMeshAgentRef
                navMeshAgentInstance = Instantiate<GameObject>(navMeshAgentRef, spawnPosition, Quaternion.identity);

                string initMessage = "启动成功。你可以说'去那里'进行导航，说'交互'和ai语音交互，说'场景分析'以了解你面前的场景，说'停下'以终止语音播报";

                if (initMessageText != null)
                {
                    initMessageText.text = initMessage;
                    if (unityWsTts != null)
                    {
                        unityWsTts.PlayTextFromTMP();
                    }
                    else
                    {
                        Debug.LogError("UnityWsTts reference is not set in inspector.");
                    }
                }
            }
        }

        void UpdateNavMeshSettingsForObjsUnderRoot ()
        {
            // Iterate all the Scene Objects
            foreach(Transform sceneObjContainer in sceneRoot.transform)
            {
                foreach(Transform sceneObj in sceneObjContainer.transform)
                {
                    NavMeshModifier nvm = sceneObj.gameObject.AddComponent<NavMeshModifier>();
                    nvm.overrideArea = true;

                    SceneUnderstandingProperties properties = sceneObj.GetComponent<SceneUnderstandingProperties>();
                    if(properties != null)
                    {
                        // Walkable = 0, Not Walkable = 1
                        // This area types are unity predefined, in the unity inspector in the navigation tab go to areas
                        // to see them
                        nvm.area = properties.suObjectKind == SceneObjectKind.Floor ? (int)AreaType.Walkable : (int)AreaType.NotWalkable;
                    }
                    else
                    {
                        // Walkable = 0, Not Walkable = 1
                        // This area types are unity predefined, in the unity inspector in the navigation tab go to areas
                        // to see them
                        nvm.area = sceneObj.parent.name == "Floor" ? (int)AreaType.Walkable : (int)AreaType.NotWalkable;
                    }
                    
                }
            }
        }
    }
}
