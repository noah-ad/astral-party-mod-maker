"""Read-only, out-of-process probe against this game's local CRI DLL. Not distributed."""
import ctypes as c
import pathlib
import sys
import time

library = c.CDLL(sys.argv[1])
movie = c.create_string_buffer(pathlib.Path(sys.argv[2]).read_bytes())
info = c.create_string_buffer(4096)
analyze = library.CRIWARE4038226E
analyze.argtypes = [c.c_void_p, c.c_void_p]
analyze.restype = c.c_int
result = analyze(movie, info)
fields = (c.c_uint32 * 12).from_buffer(info)
print("header", result, dict(zip(("reserved", "alpha", "width", "height", "display_width", "display_height", "fps_n", "fps_d", "frames", "codec", "alpha_codec", "audio"), fields)), flush=True)
if "--prepare" in sys.argv:
    def function(name, types, result=None):
        f = getattr(library, name)
        f.argtypes, f.restype = types, result
        return f

    function("CRIWARE0F3477B0", [c.c_int] * 6)(8, 8, 1, 1024, 0, 0)
    function("CRIWAREEF46D040", [])()
    function("CRIWARE5013D8DF", [c.c_int] * 4)(4, 0, 2, 4)
    function("CRIWARE88224C7A", [])()
    player = function("CRIWAREBF4BD114", [c.c_int, c.c_uint], c.c_int)(0, 1024)
    print("player", player, flush=True)
    function("CRIWAREC0FD80C9", [c.c_int, c.c_void_p, c.c_int64])(player, movie, len(movie) - 1)
    function("CRIWAREF810727B", [c.c_int])(player)
    update = function("CRIWARE05D05235", [c.c_int], c.c_int)
    last = None
    for _ in range(300):
        state = update(player)
        if state != last:
            print("status", state, flush=True)
            last = state
        if state in (4, 7, 9):
            break
        time.sleep(0.01)
    print("prepare_result", last, flush=True)
    # Process exit owns teardown; no calls into the running game process.
