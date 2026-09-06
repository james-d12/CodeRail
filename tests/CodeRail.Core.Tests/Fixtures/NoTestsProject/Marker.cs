namespace NoTestsProject;

// A plain library with no test SDK reference - `dotnet test` against this project should succeed
// (exit 0) without producing a TRX, since there are no test projects to run. Used by
// DotnetTestExecutorTests to distinguish that case from a genuine infrastructure failure.
public class Marker;
