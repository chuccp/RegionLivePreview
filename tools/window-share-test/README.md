# 窗口共享测试页

用于模拟 Google Meet 的共享窗口入口，不访问 Google，也不创建会议。

推荐直接运行：

```bash
./scripts/open-share-test.sh
```

脚本只监听本机 `127.0.0.1`，自动用 Google Chrome 打开测试页。

也可以手动启动：

```bash
python3 -m http.server 8765 --directory tools/window-share-test
```

然后打开：

```text
http://127.0.0.1:8765/
```

保持 RegionLivePreview 预览窗口打开，点击“共享屏幕”并切换到“窗口”，确认列表中能看到
`RegionLivePreview 实时预览 …`。选中后，测试页会显示共享画面和分辨率；再次点击
“停止共享”即可结束测试。

Chrome/Meet 会排除浮动层级窗口，因此 macOS 预览窗默认使用普通窗口层级，
使其可以直接出现在共享列表中。

`getDisplayMedia` 只能在安全上下文中使用，因此应通过 `localhost` 运行，不要直接以
`file://` 打开。
