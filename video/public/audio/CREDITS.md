# 音频来源

| 文件 | 用途 | 来源 | 许可 |
| --- | --- | --- | --- |
| `bgm.mp3`（未入库） | 背景音乐 | Mixkit · “Tech House” · https://mixkit.co/free-stock-music/ | Mixkit Stock Music Free License（免署名、可商用） |
| `sfx/typing.mp3` | 结果出现时的打字声 | Freesound #856166 “Rapid Laptop Keyboard Typing Click” by brktkrgll · https://freesound.org/s/856166/ | CC0 |
| `sfx/click.mp3` | 能力卡落位轻点 | Freesound #878241 “UI Keyboard Key Click” by Qvantor · https://freesound.org/s/878241/ | CC0 |

## 重新渲染前

`bgm.mp3` 因为体积和再分发原因没有入库。从 Mixkit 下载同名曲目（或任意 20 秒左右的器乐）后：

```powershell
ffmpeg -y -i <下载的曲子>.mp3 -t 20 -c:a copy public/audio/bgm.mp3
npx remotion render src/index.ts DocuFindPromo out/promo.mp4 `
  --codec=h264 --crf=18 `
  --browser-executable "C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe"
```

没有 `bgm.mp3` 也能渲染，只是没有背景音乐；打字声和点击声在 `sfx/` 里，已随仓库提供。
