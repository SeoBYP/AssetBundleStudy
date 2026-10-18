# 반복 측정(AB2, AB3, AD2)의 중앙값을 낸다. 사용법: python Measure/summarize.py
# 실행 한 번은 first_frame_ms_since_process_start 줄로 시작하므로 그 줄로 회차를 나눈다.
import re, statistics, collections, pathlib

R = pathlib.Path(__file__).parent / "results"
NUM = re.compile(r"[+-]?\d+(?:\.\d+)?")


def runs(name):
    """{(variant, key): [회차별 값]}"""
    d = collections.defaultdict(list)
    for line in open(R / f"{name}.txt", encoding="utf-8"):
        k, _, v = line.rstrip("\n").partition("\t")
        m = re.match(rf"{name}/([^#.]+)(?:#\w+)?\.(.+)$", k)
        if m and (n := NUM.search(v)):
            d[(m.group(1), m.group(2))].append(float(n.group()))
    return d


def show(name, keys, skip_first=False):
    if not (R / f"{name}.txt").exists():
        return
    for (variant, key), xs in sorted(runs(name).items()):
        if any(re.fullmatch(k, key) for k in keys):
            ys = xs[1:] if skip_first and len(xs) > 1 else xs
            print(f"{name} {variant:9} {key:32} median={statistics.median(ys):9.2f}  all={xs}")


show("AB3", [r"\w+\.open_ms", r"\w+\.loadasset_ms", r"\w+\.mem_after_open"])
show("AD2", [r"load_one", r"init"])
show("AB2", [r"first_frame_ms_since_process_start", r"first_frame_realtimeSinceStartup_ms"], skip_first=True)
