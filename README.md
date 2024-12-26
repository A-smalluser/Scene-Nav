# Scene-Nav

## 项目描述
此项目基于HoloLens2的场景理解示例进行改进，在Navmesh场景中增添了动态导航、重定向、语音交互、场景分析和语音打断功能。

## 开发环境
- Unity 2020.3.12f1

## 如何使用
1. 导入 Assets/SceneUnderstanding/Examples/NavMesh/Scenes/NavMesh-Simple.unity 场景
2. 在 VoiceInter Manager 中输入你的 OpenAI 密钥
3. 在 TTS Manager 中输入科大讯飞的密钥等信息
4. 在 Project Settings 中确认勾选以下权限：
   - InternetClien
   - InternetClientServer
   - PicturesLibrary
   - WebCam
   - Microphone
   - SpatialPerception
5. Build Settings 配置：
   - 平台选择：UWP
   - Target Device：HoloLens
   - Architecture：ARM64
   - Build Type：D3D Project

## 模块介绍
- **NavMesh**: 负责导航，生成一个预制体，导航到摄像头与地图的焦点，并生成导航路线信息
- **VoiceInter Manager**: 负责语音交互，包括语音识别和连接OpenAI
- **Canvas Manager**: 负责控制组件，无实际用处
- **TTS Manager**: 负责使用科大讯飞进行语音合成
- **Photo Manager**: 负责拍照并场景分析
