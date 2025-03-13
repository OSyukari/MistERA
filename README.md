# MistEra Build

迷雾Era的试玩build。请直接去[Release页](https://gitgud.io/OS74/mistera/-/releases/permalink/latest)下载最新的Package（已经build好的压缩包）

Repo中只保存了代码和Json文件，并未包括dll等（会导致仓库容量无法抑制地膨胀）

默认分辨率为1920x1017（1080p窗口）

如果运行性能不佳（例如按了任何按钮后，尽管没有因为房间内一瞬间多出4-5个角色导致读取立绘，游戏反应却依旧很慢的话）请告知您的电脑配置以及资源管理器显示该程序的资源占用。

## 文件结构
以下路径为build内，未build过的结构的话把\Mist Era_Data换成\Assets

- \Mist Era_Data\Data\Characters

    - 预设NPC的配置文件夹，只要是在该目录内深度一层以内都会被自动索取（该目录内文件夹内的文件夹大概不会被读取）

- \Mist Era_Data\Data\Defs

    - 基础配置信息，指令 物品 地图 口上 全都在这里

- \Mist Era_Data\Data\Dictionary

    - 外部翻译用信息，游戏会根据当前选择的词典自动更换取用的翻译文字

- \Mist Era_Data\Presets 

    - 玩家预设的储存位置

- \Mist Era_Data\Save

    - 存档位置

- %AppData%\LocalLow\Silent Forest\Mist Era

    - 玩家Logs保存位置（报错用）

- \Mist Era_Data\Managed\Assembly-CSharp.dll
    - C#代码。载入dnspy或者Visual Studio Reference即可读取全部内容。