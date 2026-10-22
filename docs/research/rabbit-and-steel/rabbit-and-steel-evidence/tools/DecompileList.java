// Decompiles each "hexaddr name" line of the targets file into <outDir>/<name>.c (read-only).
// Args: <targetsFile> <outDir>
import ghidra.app.script.GhidraScript;
import ghidra.app.decompiler.*;
import ghidra.program.model.address.Address;
import ghidra.program.model.listing.Function;
import java.io.*;
import java.nio.file.*;
import java.util.*;

public class DecompileList extends GhidraScript {
    @Override
    public void run() throws Exception {
        String[] args = getScriptArgs();
        List<String> lines = Files.readAllLines(Paths.get(args[0]));
        File out = new File(args[1]);
        out.mkdirs();
        DecompInterface ifc = new DecompInterface();
        ifc.openProgram(currentProgram);
        for (String line : lines) {
            String[] p = line.trim().split("\\s+");
            if (p.length < 2) continue;
            Address a = toAddr(Long.parseUnsignedLong(p[0], 16));
            Function f = getFunctionAt(a);
            if (f == null) f = createFunction(a, p[1]);
            if (f == null) { println("no function at " + p[0]); continue; }
            DecompileResults r = ifc.decompileFunction(f, 600, monitor);
            String c = r.decompileCompleted() ? r.getDecompiledFunction().getC() : ("// failed: " + r.getErrorMessage());
            Files.writeString(Paths.get(out.getPath(), p[1] + ".c"), "// " + p[0] + " " + p[1] + "\n" + c);
            println("decompiled " + p[1]);
        }
        ifc.dispose();
    }
}
