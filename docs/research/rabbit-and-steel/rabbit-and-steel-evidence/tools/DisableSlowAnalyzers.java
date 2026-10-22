// Pre-script: turn off the same slow analyzers REA used, before auto-analysis.
import ghidra.app.script.GhidraScript;

public class DisableSlowAnalyzers extends GhidraScript {
    @Override
    public void run() throws Exception {
        String[] names = { "WindowsResourceReference", "Decompiler Switch Analysis", "Stack", "Function ID", "Embedded Media" };
        for (String n : names) {
            setAnalysisOption(currentProgram, n, "false");
            println("disabled " + n);
        }
    }
}
