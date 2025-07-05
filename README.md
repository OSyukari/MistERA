# MistEra Build

迷雾Era的试玩build。请直接去[Release页](https://gitgud.io/OS74/mistera/-/releases/permalink/latest)下载最新的Package（已经build好的压缩包）

关于每版本实装的内容简介，请看[CHANGELOG.md](https://gitgud.io/OS74/mistera/-/blob/master/CHANGELOG.md)

关于下版本将要实装的内容，请看[WIKI:预定实装机能](https://gitgud.io/OS74/mistera/-/wikis/release_plan)

Repo中只保存了代码和Json文件，并未包括dll等（会导致仓库容量无法抑制地膨胀）

默认分辨率为1920x1017（1080p窗口）

如果运行性能不佳，请告知您的电脑配置以及资源管理器显示该程序的资源占用，可以的话请附上存档。

## 文件结构
以下路径为build内，未build过的结构的话把/Mist Era_Data换成/Assets

- /Data/
    - 游戏JSON数据文件夹，只要是在该目录无论深度都会被读取

- /Presets/
    - 玩家用的预设角色存储文件夹，只要是在该目录无论深度都会被当作角色文件读取

- /Save/
    - 存档位置

- /Mist Era_Data/UserPrefs.json
    - 程序设定文件，会在首次运行时自动生成一份

- %AppData%/LocalLow/Silent Forest/Mist Era
    - 玩家Logs保存位置（报错用）



