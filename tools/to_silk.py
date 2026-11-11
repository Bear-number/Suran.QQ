#!/usr/bin/env python3
# -*- coding: utf-8 -*-
r"""pcm → silk 封装器：给 Suran.QQ 的「语音·silk 编码器」用。
（ffmpeg 那一步由插件先跑好，这里只负责 pcm → silk。）

为什么需要它：官方 file_type=3 的语音条要 silk，而 **ffmpeg 只能解码 silk、不能编码**，
编码必须用 Silk 专有实现（pilk 是它的 Rust 绑定 + Python 轮子）。

插件配置（用 Alife 自带的解释器）：
    语音·silk 编码器 = D:\Alife\Runtime\Python312\python.exe
    语音·编码器参数   = "D:\Alife\Storage\Plugins\Suran.QQ\tools\to_silk.py" {in} {out}

装 pilk：
    D:\Alife\Runtime\Python312\python.exe -m pip install pilk

现实提醒（2026-10-23 本机实测）：pilk 的 Windows 轮子只出到 **cp311**，3.12 只能拿到源码包，
装它要 Rust 工具链现编。装不上时有两条退路：
    ① 打开配置「语音·放行非 silk 直传」——官方富媒体概述页写明语音支持 silk/mp3/wav/ogg，
       很可能压根不用转码（限制表却只写 silk，两处矛盾，拿真接口试一次就有答案）；
    ② 确实要转码，就把上面两项换成一个有 cp311 轮子的解释器。

用法：python to_silk.py <in.pcm> <out.silk> [pcm_rate=24000]
退出码：0 成功（stdout 只打时长秒数）/ 2 参数不对 / 3 没装 pilk / 4 编码失败
pcm_rate 必须与 ffmpeg 那一步的 -ar 完全一致（插件里用 24000），不然语速会变调。
"""
import os
import sys

DEFAULT_RATE = 24000


def main(argv):
    if len(argv) < 3:
        print("用法：python to_silk.py <in.pcm> <out.silk> [pcm_rate]", file=sys.stderr)
        return 2
    pcm, silk = argv[1], argv[2]
    rate = int(argv[3]) if len(argv) > 3 and argv[3].strip().isdigit() else DEFAULT_RATE
    if os.path.exists(pcm) == False or os.path.getsize(pcm) == 0:
        print("pcm 不存在或是空的，ffmpeg 那一步大概失败了", file=sys.stderr)
        return 4
    try:
        import pilk
    except ImportError:
        print("这个解释器没装 pilk：先用同一个 python.exe 执行 -m pip install pilk"
              "（pilk 只出到 cp311 的 Windows 轮子，3.12 起要 Rust 现编）", file=sys.stderr)
        return 3
    try:
        duration = pilk.encode(pcm, silk, pcm_rate=rate, tencent=True)
    except Exception as error:
        print("pilk 编码失败：%s: %s" % (type(error).__name__, error), file=sys.stderr)
        return 4
    if os.path.exists(silk) == False or os.path.getsize(silk) == 0:
        print("pilk 返回了但没产出 silk 文件", file=sys.stderr)
        return 4
    print(duration)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv))
