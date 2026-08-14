# DeepSeek 主体 + 视觉外挂：安装与接入说明

目标架构：**主模型保持 DeepSeek（纯文本），识图时由 MCP 工具 `mcp__vision__describe_image` 把图片发给视觉端点，拿回纯文本描述。**

- 脚本：[`dsh-vision-mcp.cjs`](../dsh-vision-mcp.cjs)（零依赖 MCP stdio 服务器，OpenAI 兼容协议）
- 插件：`dsh-vision-inline`（消息管线插件：贴进对话框的图片自动转文字描述）
- 接入：`%USERPROFILE%\.dsh\profiles\web\cordis.patch.yml` 的 `mcp-vision`、`vision-inline` 行
- 用法：**直接往对话框粘贴/拖入一张或多张图片发送即可**；也可 `Win+Shift+S` 截图后说「看图」（剪贴板路线）
- 本地测试样例（如有）统一放在 [`samples/vision/`](../samples/vision/)；图片默认被 Git 忽略，仅目录占位文件会发布。

## 对话框贴图直发（vision-inline 插件，主用法）

**必须在新会话中使用**（默认模型/闸门配置只在会话创建时生效）。

- 往输入框粘贴或拖入一张**或多张**图片，可附带文字，直接发送；
- 插件在消息持久化前把每张图交给视觉端点（GLM-4.6V-Flash），替换为 `[用户发送的图片，由视觉助手识别]` 文字描述；
- DeepSeek 全程只看到文字；日志里存的也是文字版（图片不进历史、不反复计费）；
- `read_image` 工具结果里的图片块同样被自动转成描述；
- 视觉失败（429 挤爆/网络）时降级为占位文字，消息照常送达，不会卡死会话；
- 描述按 attachmentId 缓存，同一张图不会重复调用视觉端点。

生效前提：`settings.yaml` 的 `llm-pi-ai.providers.deepseek` 路由存在（已配置），新会话默认模型走
pi-ai `deepseek` 路由（`agent-default-model.provider: deepseek`）。推理档位因 pi-ai 目录限制由
`max` 调整为 `high`。

## 当前模式：云端免费档（方案 C，默认生效）

端点：智谱开放平台 `https://open.bigmodel.cn/api/paas/v4`，模型 `glm-4.6v-flash`（**完全免费**，128K 上下文，原生工具调用）。
密钥不写进任何文件：通过用户环境变量 `VISION_API_KEY` 传入。

```powershell
# 注册 https://open.bigmodel.cn 拿到 key 后，PowerShell 执行一次：
[Environment]::SetEnvironmentVariable('VISION_API_KEY','你的key','User')
# 然后【新开终端】重启 dsh web 生效
```

智谱的 key 形如 `xxxxx.yyyyyy`（含一个点），整串填入即可。
免费档有调用频率限制（RPM/TPM），个人识图用量完全够。

换模型：智谱平台还有 `glm-4.6v-flashx`（轻量高速，付费）与 `glm-4.6v`（高性能，付费）；
想换回硅基流动免费档（`Qwen/Qwen2.5-VL-7B-Instruct`）则把 `VISION_BASE_URL` 改成
`https://api.siliconflow.cn/v1`。改 `cordis.patch.yml` 对应字段后重启 dsh web。

## 本地模式（方案 B，已暂缓，随时可切回）

### 本机配置（已勘察）

- GPU：NVIDIA RTX 5060 Laptop，8GB 显存（桌面已占用约 2GB）
- CPU：AMD Ryzen 9 8940HX；内存约 32GB
- 结论：跑 3–4B 档视觉模型最稳；7–8B 档显存偏紧。

### 第 1 步：安装 Ollama 到 D 盘（免管理员，便携版）

1. 下载：https://ollama.com/download/ollama-windows-amd64.zip（慢就用迅雷/IDM 多线程，
   或 GitHub 代理 `https://mirror.ghproxy.com/https://github.com/ollama/ollama/releases/download/<版本号>/ollama-windows-amd64.zip`）
2. 解压到 `D:\ollama`（应有 `D:\ollama\ollama.exe`）。

### 第 2 步：设置环境变量（PowerShell 执行一次）

```powershell
[Environment]::SetEnvironmentVariable('OLLAMA_MODELS','D:\ollama\models','User')
[Environment]::SetEnvironmentVariable('OLLAMA_NUM_PARALLEL','1','User')
[Environment]::SetEnvironmentVariable('OLLAMA_MAX_LOADED_MODELS','1','User')
$p = [Environment]::GetEnvironmentVariable('Path','User')
if ($p -notlike '*D:\ollama*') { [Environment]::SetEnvironmentVariable('Path', ($p.TrimEnd(';') + ';D:\ollama'), 'User') }
```

### 第 3 步：启动并拉取模型

```powershell
D:\ollama\ollama.exe serve          # 1 号终端，挂着别关
# 另开 2 号终端：
D:\ollama\ollama.exe pull qwen2.5-vl:3b
D:\ollama\ollama.exe run qwen2.5-vl:3b "你好"
```

模型下载慢的国内路线：魔搭（ModelScope）搜 `Qwen/Qwen2.5-VL-3B-Instruct-GGUF` 下
`qwen2.5-vl-3b-instruct-q4_k_m.gguf`，然后：
```
# Modelfile 内容：FROM D:\ollama\models\qwen2.5-vl-3b-instruct-q4_k_m.gguf
D:\ollama\ollama.exe create qwen2.5-vl:3b -f Modelfile
```

### 切回本地

把 `cordis.patch.yml` 里 `mcp-vision` 的 `env` 改回：

```yaml
VISION_BASE_URL: 'http://127.0.0.1:11434/v1'
VISION_MODEL: 'qwen2.5-vl:3b'
# 删掉 VISION_API_KEY 行
```

重启 dsh web。

## 模型选型参考（本地）

| 模型 | 显存(Q4) | 说明 |
|---|---|---|
| `qwen2.5-vl:3b`（默认） | 约 2GB | 舒服，中文截图/UI 够用 |
| `qwen2.5-vl:7b` | 约 5.6GB | 质量更好，但 8GB 显卡上偏紧 |
| `gemma3:4b` | 约 3.3GB | 备选 |

## 故障排查

- 工具报「endpoint not reachable」→ 本地模式时 Ollama 没在跑（`ollama serve`）。
- 工具报「HTTP 429 访问量过大」→ 免费档高峰拥堵，脚本已内置自动退避重试（3 次：3s/8s/15s）；仍失败就是真挤爆，等几分钟再试。
- 工具报「HTTP 401」→ `VISION_API_KEY` 环境变量没设，或设了之后没有重启 dsh web。
- 工具报「HTTP 404」→ `VISION_MODEL` 名字不对（当前应为 `glm-4.6v-flash`）。
- 回复慢 → 云端模式一般 2–10 秒；本地模式首次要加载模型进显存。
- 服务器日志 → dsh web 终端里 `[dsh-vision] ...` 开头的行。
