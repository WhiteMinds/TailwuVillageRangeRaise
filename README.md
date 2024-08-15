## 开发流程

1. 创建一个软链接，将项目目录中的 Mod 文件夹链接到游戏目录中的 Mod\Test 文件夹。

```shell
# 例子
# 管理员权限
cmd /c mklink /d "Z:\SteamLibrary\steamapps\common\The Scroll Of Taiwu\Mod\TailwuVillageRangeRaise" Z:\Projects\TailwuVillageRangeRaise\Mod
```

2. 使用 VS2022 打开项目，配置好引用文件的路径。

3. F7 编译，此时成功编译即可在游戏中看到 Mod。
