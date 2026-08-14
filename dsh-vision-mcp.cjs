#!/usr/bin/env node
// dsh-vision-mcp.cjs — 零依赖 MCP(stdio) 服务器：给纯文本主模型(如 DeepSeek)提供一个
// `describe_image` 工具，把图片发给任意 OpenAI 兼容的视觉端点(默认本地 Ollama)，拿回纯文本描述。
//
// 环境变量(全部可选，也可在 dsh 的 cordis.patch.yml 里给)：
//   VISION_BASE_URL    端点 base，默认 http://127.0.0.1:11434/v1
//   VISION_MODEL       模型 id，默认 qwen2.5-vl:3b
//   VISION_API_KEY     可选 Bearer key（本地 Ollama 不需要）
//   VISION_MAX_TOKENS  输出上限，默认 1024
//   VISION_TIMEOUT_MS  超时毫秒，默认 180000
'use strict';

const fs = require('fs');
const path = require('path');

const BASE_URL = (process.env.VISION_BASE_URL || 'http://127.0.0.1:11434/v1').replace(/\/+$/, '');
const MODEL = process.env.VISION_MODEL || 'qwen2.5-vl:3b';
const API_KEY = process.env.VISION_API_KEY || '';
const MAX_TOKENS = Number(process.env.VISION_MAX_TOKENS || '1024');
const TIMEOUT_MS = Number(process.env.VISION_TIMEOUT_MS || '180000');
const MAX_FILE_BYTES = 20 * 1024 * 1024;

const MIME_BY_EXT = {
  '.png': 'image/png',
  '.jpg': 'image/jpeg',
  '.jpeg': 'image/jpeg',
  '.webp': 'image/webp',
  '.gif': 'image/gif',
};

const TOOL = {
  name: 'describe_image',
  description:
    'Look at a local image file with a vision model and return a detailed Chinese text description. ' +
    'Use this whenever you need to actually "see" an image (screenshot, UI mockup, chart, scan, photo): ' +
    'it sends the image to a vision endpoint (a local Ollama server by default) and returns plain text, ' +
    'so it works even though the main model cannot accept images directly.',
  inputSchema: {
    type: 'object',
    properties: {
      file_path: {
        type: 'string',
        description: 'Path to the image file (PNG / JPEG / WebP / GIF).',
      },
      question: {
        type: 'string',
        description: 'Optional specific question about the image, in Chinese or English.',
      },
    },
    required: ['file_path'],
  },
};

function buildPrompt(question) {
  let p =
    '你是图像理解助手。请仔细观察这张图片，用中文输出一份准确、有条理的描述。\n' +
    '要求：\n' +
    '1. 先用一句话总述：这是什么图（截图/界面/图表/文档/照片），主体是什么。\n' +
    '2. 如果图中有文字，请逐段完整转录（保持原文，不要翻译、不要改写），并注明大致位置，例如“标题”“左上角”“按钮”。\n' +
    '3. 描述关键视觉元素：布局结构、颜色、图表数据、界面控件等。\n' +
    '4. 直接输出描述正文，不要客套铺垫。\n';
  const q = question === undefined || question === null ? '' : String(question).trim();
  if (q) {
    p += '\n用户的具体问题：' + q + '\n请围绕该问题重点回答。\n';
  }
  return p;
}

async function describeImage(filePath, question) {
  if (typeof filePath !== 'string' || !filePath.trim()) {
    throw new Error('file_path must be a non-empty string');
  }
  const ext = path.extname(filePath).toLowerCase();
  const mime = MIME_BY_EXT[ext];
  if (!mime) {
    throw new Error('unsupported image type "' + (ext || '(none)') + '": only PNG/JPEG/WebP/GIF are accepted');
  }
  let stat;
  try {
    stat = fs.statSync(filePath);
  } catch (e) {
    throw new Error('cannot read "' + filePath + '": ' + (e.code === 'ENOENT' ? 'not found' : e.message));
  }
  if (!stat.isFile()) {
    throw new Error('cannot read "' + filePath + '": not a regular file');
  }
  if (stat.size > MAX_FILE_BYTES) {
    throw new Error('cannot read "' + filePath + '": image is ' + stat.size + ' bytes, over the ' + MAX_FILE_BYTES + ' byte limit');
  }
  const base64 = fs.readFileSync(filePath).toString('base64');

  const body = {
    model: MODEL,
    messages: [
      {
        role: 'user',
        content: [
          { type: 'text', text: buildPrompt(question) },
          { type: 'image_url', image_url: { url: 'data:' + mime + ';base64,' + base64 } },
        ],
      },
    ],
    stream: false,
    max_tokens: MAX_TOKENS,
  };

  const RETRY_DELAYS_MS = [3000, 8000, 15000];
  let resp;
  for (let attempt = 0; ; attempt++) {
    try {
      resp = await fetch(BASE_URL + '/chat/completions', {
        method: 'POST',
        headers: {
          'Content-Type': 'application/json',
          ...(API_KEY ? { Authorization: 'Bearer ' + API_KEY } : {}),
        },
        body: JSON.stringify(body),
        signal: AbortSignal.timeout(TIMEOUT_MS),
      });
    } catch (e) {
      const code = e && e.cause && e.cause.code;
      if (code === 'ECONNREFUSED') {
        throw new Error('vision endpoint ' + BASE_URL + ' is not reachable — is Ollama running? (start it with "ollama serve")');
      }
      if (code === 'ENOTFOUND') {
        throw new Error('cannot resolve vision endpoint host in ' + BASE_URL);
      }
      if (e && e.name === 'TimeoutError') {
        throw new Error('vision request timed out after ' + TIMEOUT_MS + 'ms');
      }
      throw new Error('vision request failed: ' + (e && e.message ? e.message : e));
    }
    if (resp.ok || resp.status !== 429 || attempt >= RETRY_DELAYS_MS.length) break;
    console.error('[dsh-vision] HTTP 429 (free tier busy), retry ' + (attempt + 1) + '/' + RETRY_DELAYS_MS.length + ' in ' + RETRY_DELAYS_MS[attempt] + 'ms');
    await new Promise((resolve) => setTimeout(resolve, RETRY_DELAYS_MS[attempt]));
  }

  if (!resp.ok) {
    let detail = '';
    try {
      detail = (await resp.text()).slice(0, 400);
    } catch {}
    throw new Error('vision endpoint replied HTTP ' + resp.status + (detail ? ': ' + detail : ''));
  }

  let data;
  try {
    data = await resp.json();
  } catch {
    throw new Error('vision endpoint returned invalid JSON');
  }
  const content =
    data && data.choices && data.choices[0] && data.choices[0].message && data.choices[0].message.content;
  let text;
  if (typeof content === 'string') {
    text = content;
  } else if (Array.isArray(content)) {
    text = content
      .filter((p) => p && p.type === 'text')
      .map((p) => p.text)
      .join('\n');
  } else {
    text = '';
  }
  text = (text || '').trim();
  if (!text) {
    throw new Error('vision model "' + MODEL + '" returned no text');
  }
  return text;
}

// ---- 极简 MCP stdio 传输（JSON-RPC 2.0，换行分隔） ----
function send(payload) {
  process.stdout.write(JSON.stringify(payload) + '\n');
}

async function handle(msg) {
  if (!msg || typeof msg !== 'object') return;
  if (msg.method === 'initialize') {
    const requested =
      msg.params && typeof msg.params.protocolVersion === 'string' ? msg.params.protocolVersion : '2024-11-05';
    return send({
      jsonrpc: '2.0',
      id: msg.id,
      result: {
        protocolVersion: requested,
        capabilities: { tools: {} },
        serverInfo: { name: 'dsh-vision', version: '1.0.0' },
      },
    });
  }
  if (msg.method === 'tools/list') {
    return send({ jsonrpc: '2.0', id: msg.id, result: { tools: [TOOL] } });
  }
  if (msg.method === 'tools/call') {
    const name = msg.params && msg.params.name;
    const args = (msg.params && msg.params.arguments) || {};
    if (name !== TOOL.name) {
      return send({
        jsonrpc: '2.0',
        id: msg.id,
        result: { content: [{ type: 'text', text: 'unknown tool: ' + name }], isError: true },
      });
    }
    try {
      const text = await describeImage(args.file_path, args.question);
      return send({ jsonrpc: '2.0', id: msg.id, result: { content: [{ type: 'text', text }] } });
    } catch (e) {
      return send({
        jsonrpc: '2.0',
        id: msg.id,
        result: {
          content: [{ type: 'text', text: 'describe_image failed: ' + (e && e.message ? e.message : e) }],
          isError: true,
        },
      });
    }
  }
  if (msg.method === 'ping') {
    return send({ jsonrpc: '2.0', id: msg.id, result: {} });
  }
  // 通知(无 id)一律忽略；未知请求回 method not found。
  if (msg.id !== undefined && msg.id !== null) {
    return send({ jsonrpc: '2.0', id: msg.id, error: { code: -32601, message: 'unknown method: ' + msg.method } });
  }
}

let buffer = '';
process.stdin.setEncoding('utf8');
process.stdin.on('data', (chunk) => {
  buffer += chunk;
  let idx;
  while ((idx = buffer.indexOf('\n')) >= 0) {
    const line = buffer.slice(0, idx).trim();
    buffer = buffer.slice(idx + 1);
    if (!line) continue;
    let msg;
    try {
      msg = JSON.parse(line);
    } catch {
      continue;
    }
    Promise.resolve(handle(msg)).catch((e) => {
      if (msg.id !== undefined && msg.id !== null) {
        send({ jsonrpc: '2.0', id: msg.id, error: { code: -32603, message: String((e && e.message) || e) } });
      }
      console.error('[dsh-vision] handler error:', (e && e.message) || e);
    });
  }
});
process.stdin.on('end', () => {
  // 不主动 exit：让挂起的请求自然结束后，事件循环清空、进程自行退出。
  // （若 stdin 先关闭，立刻 exit 会杀掉仍在等待网络回复的调用。）
});
process.on('SIGTERM', () => process.exit(0));
console.error('[dsh-vision] up: endpoint=' + BASE_URL + ' model=' + MODEL);
