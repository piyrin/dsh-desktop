# 可选视觉集成指南

DSH Desktop 本身不捆绑视觉模型、视觉服务或图片自动转换插件。本指南说明如何把仓库根目录的 [`dsh-vision-mcp.cjs`](../dsh-vision-mcp.cjs) 注册为可选 MCP stdio 服务，让纯文本主模型通过 `mcp__vision__describe_image` 获取图片的纯文本描述。

该脚本位于相对于仓库根目录的精确路径 `dsh-vision-mcp.cjs`。为兼容已有 DSH profile，请保留这个根目录位置。

## 工作方式

- MCP 工具名为 `describe_image`；DSH 通常将其显示为 `mcp__vision__describe_image`。
- 工具读取本地 PNG、JPEG、WebP 或 GIF 文件，单文件上限为 20 MiB。
- 图片会发送到兼容 OpenAI `POST /chat/completions` 接口、且支持图片 data URL 的视觉端点。
- 视觉端点返回的文本会作为 MCP 文本结果交给主模型；脚本不会让主模型直接接收图片。
- 本仓库不包含 `dsh-vision-inline`。如果另行安装图片自动转换插件，请按该插件自己的文档配置并单独验证。

## 前置条件

- 已安装并可运行 DSH，以及 DSH 使用的现代 Node.js 运行时。
- 一个可访问的视觉端点：可以是本地 Ollama，也可以是兼容上述接口的远程服务。
- 端点中已安装或已启用与 `VISION_MODEL` 对应的视觉模型。
- 可以编辑目标 DSH profile 的用户配置，例如 `%USERPROFILE%\.dsh\profiles\web\cordis.patch.yml`。
- 使用远程端点时，拥有该服务要求的凭据，并了解其数据处理、配额和计费条款。

服务可用性、模型名称、配额和价格可能变化；请以所选提供方的当前文档为准。

## 环境变量

| 变量 | 默认值 | 用途 |
|---|---|---|
| `VISION_BASE_URL` | `http://127.0.0.1:11434/v1` | OpenAI 兼容 API 基地址 |
| `VISION_MODEL` | `qwen2.5-vl:3b` | 视觉模型 ID |
| `VISION_API_KEY` | 未设置 | 可选 Bearer 凭据；本地 Ollama 通常不需要 |
| `VISION_MAX_TOKENS` | `1024` | 最大文本输出 token 数 |
| `VISION_TIMEOUT_MS` | `180000` | 单次请求超时毫秒数 |

## 选择视觉端点

### 本地 Ollama 示例

安装 Ollama 并准备与脚本默认值匹配的模型后，启动服务：

```powershell
ollama pull qwen2.5-vl:3b
ollama serve
```

使用默认本地端点时，可以省略 `VISION_BASE_URL`、`VISION_MODEL` 和 `VISION_API_KEY`。如果使用其他本地模型，请显式设置对应的模型 ID。

### 远程兼容端点示例

不要把真实凭据写进仓库或提交到 profile 文件。可以先把凭据保存到操作系统的用户环境中：

```powershell
$visionApiKey = Read-Host 'Vision API key'
[Environment]::SetEnvironmentVariable('VISION_API_KEY', $visionApiKey, 'User')
Remove-Variable visionApiKey
```

环境变量变更后，请启动新的 DSH 进程，使其读取新值。

## 注册 MCP 服务

在目标 profile 的 `cordis.patch.yml` 中添加一个 MCP 客户端条目。下面是远程端点示例；请将占位符替换为自己的值：

```yaml
- insert:
    - id: mcp-vision
      name: '@deepseek-ai/dsh-mcp-client'
      config:
        serverName: vision
        transport: stdio
        command: !!js process.execPath
        args: ['<repo-root>/dsh-vision-mcp.cjs']
        env:
          VISION_BASE_URL: 'https://vision-provider.example/v1'
          VISION_MODEL: '<provider-model-id>'
          VISION_API_KEY: !!js process.env.VISION_API_KEY
          VISION_MAX_TOKENS: '1024'
          VISION_TIMEOUT_MS: '180000'
        toolCallTimeoutMs: 180000
```

`<repo-root>` 是占位符，不应原样保留。请在用户自己的、未提交的 profile 配置中替换为本地仓库根目录，并确保最终参数指向根目录的 `dsh-vision-mcp.cjs`。本地 Ollama 配置可以保留同一条目并删除 `env`，让脚本采用默认值。

修改 profile 后，重启 DSH 并创建新会话，以确保配置和工具目录重新加载。

## 安全注意事项

- 远程视觉服务会接收图片内容。发送截图、文档或照片前，确认其中不含不应离开设备的数据。
- 不要把 API key、访问令牌或真实 profile 凭据写入仓库、样例图片、命令历史或故障报告。
- `VISION_API_KEY` 仅应从进程环境读取；YAML 示例使用 `process.env.VISION_API_KEY`，不包含实际值。
- 本地测试图片放在 [`samples/vision/`](../samples/vision/)；该目录中的图片默认被 Git 忽略，仅 `.gitkeep` 被跟踪。
- 分享日志前先检查端点 URL、文件路径和其他环境信息是否适合公开。

## 验证步骤

1. 启动本地视觉服务，或确认远程兼容端点可访问。
2. 重启 DSH，并查看 stderr 或 DSH 日志中是否出现 `[dsh-vision] up: endpoint=... model=...`。
3. 把一张非敏感测试图保存为 `samples/vision/local-check.png`，并确认它不会被 Git 跟踪：

   ```powershell
   git check-ignore --no-index samples/vision/local-check.png
   ```

4. 在新会话中调用 `mcp__vision__describe_image`，传入该文件路径和一个具体问题。
5. 确认工具返回纯文本描述；再检查 `git status --short`，确保测试图片和凭据没有进入待提交内容。

## 故障排查

- `endpoint ... is not reachable`：确认本地服务已启动，或检查 `VISION_BASE_URL`、DNS、防火墙和代理设置。
- HTTP 401/403：确认 DSH 进程可以读取 `VISION_API_KEY`，且凭据适用于当前端点。
- HTTP 404：确认 API 基地址包含正确的版本路径，并核对 `VISION_MODEL`。
- HTTP 429：脚本会按 3、8、15 秒进行最多三次退避重试；持续失败时请等待或检查提供方配额。
- 请求超时：检查模型是否已加载、图片大小和网络状态；需要时调整 `VISION_TIMEOUT_MS`。
- `unsupported image type`：仅使用 PNG、JPEG、WebP 或 GIF。
- `returned no text`：确认所选模型支持视觉输入和非流式文本响应。

## 上游与归属

该视觉集成是可选的独立兼容层，不是 DeepSeek、DSH 上游项目、Ollama 或任何视觉服务提供方的官方组件，也不表示获得其认可。
