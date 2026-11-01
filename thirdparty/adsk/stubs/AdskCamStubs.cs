// adsk.cam stub surface — mirrors the Python reference's CAM handlers
// (reference/fusion360-mcp-server/addon/server/command_handler.py:
// _get_cam, cam_list_setups, cam_list_operations, cam_get_operation_info,
// cam_create_setup, cam_create_operation, cam_generate_toolpath,
// cam_post_process). The adsk.cam .NET API is a documented assumption (T8):
// it is inferred from the Python binding, which reflects the genuine API.
namespace adsk.cam
{
    using adsk.core;

    /// <summary>Operation type ids (reference adsk.cam.OperationTypes).</summary>
    public enum OperationTypes
    {
        MillingOperation = 0,
        TurningOperation = 1,
        JetOperation = 2,
    }

    /// <summary>Post-processor output units (reference adsk.cam.PostOutputUnitOptions).</summary>
    public enum PostOutputUnitOptions
    {
        MillimetersOutput = 0,
        InchesOutput = 1,
    }

    /// <summary>Async CAM generation future (reference future.isCompleted / future.wait()).</summary>
    public sealed class CamFuture
    {
        public bool IsCompleted => throw new System.NotSupportedException("adsknet stub");
        public void Wait() => throw new System.NotSupportedException("adsknet stub");
    }

    /// <summary>adsk.cam.CamSetupInput (reference setup_input.models = [body]; setup_input.name = name).</summary>
    public sealed class CamSetupInput
    {
        /// <summary>The setups to machine (an ObjectCollection of bodies — reference setup_input.models = [body]).</summary>
        public ObjectCollection? Models { get => throw new System.NotSupportedException("adsknet stub"); set => throw new System.NotSupportedException("adsknet stub"); }
        public string? Name { get => throw new System.NotSupportedException("adsknet stub"); set => throw new System.NotSupportedException("adsknet stub"); }
    }

    /// <summary>adsk.cam.CamSetup (cam.setups.item(i)).</summary>
    public sealed class CamSetup
    {
        public string Name => throw new System.NotSupportedException("adsknet stub");
        public bool IsValid => throw new System.NotSupportedException("adsknet stub");
        public CamOperationCollection Operations => throw new System.NotSupportedException("adsknet stub");

        /// <summary>Setup-level parameters (job_stock* — reference setup.parameters).</summary>
        public CamParameterCollection Parameters => throw new System.NotSupportedException("adsknet stub");
    }

    /// <summary>adsk.cam.CamSetupCollection.</summary>
    public sealed class CamSetupCollection
    {
        public int Count => throw new System.NotSupportedException("adsknet stub");
        public CamSetup Item(int index) => throw new System.NotSupportedException("adsknet stub");

        /// <summary>reference cam.setups.createInput(op_type).</summary>
        public CamSetupInput CreateInput(OperationTypes operationType)
            => throw new System.NotSupportedException("adsknet stub");

        /// <summary>reference setup = cam.setups.add(setup_input).</summary>
        public CamSetup Add(CamSetupInput input) => throw new System.NotSupportedException("adsknet stub");
    }

    /// <summary>adsk.cam.CamOperation (setup.operations.item(i)).</summary>
    public sealed class CamOperation
    {
        public string Name => throw new System.NotSupportedException("adsknet stub");
        public bool HasToolpath => throw new System.NotSupportedException("adsknet stub");
        public bool IsValid => throw new System.NotSupportedException("adsknet stub");

        /// <summary>The operation's tool (reference op.tool.description — may be null).</summary>
        public CamTool? Tool => throw new System.NotSupportedException("adsknet stub");

        /// <summary>Operation parameters (reference iterates op.parameters: param.name / param.expression).</summary>
        public CamParameterCollection Parameters => throw new System.NotSupportedException("adsknet stub");
    }

    /// <summary>adsk.cam.CamTool (reference op.tool.description).</summary>
    public sealed class CamTool
    {
        public string? Description => throw new System.NotSupportedException("adsknet stub");
    }

    /// <summary>adsk.cam.CamOperationInput (reference op_input = setup.operations.createInput(strategy); op_input.name = name).</summary>
    public sealed class CamOperationInput
    {
        public string? Name { get => throw new System.NotSupportedException("adsknet stub"); set => throw new System.NotSupportedException("adsknet stub"); }
    }

    /// <summary>adsk.cam.CamOperationCollection (setup.operations).</summary>
    public sealed class CamOperationCollection
    {
        public int Count => throw new System.NotSupportedException("adsknet stub");
        public CamOperation Item(int index) => throw new System.NotSupportedException("adsknet stub");

        /// <summary>reference op_input = setup.operations.createInput(strategy).</summary>
        public CamOperationInput CreateInput(string strategy)
            => throw new System.NotSupportedException("adsknet stub");

        /// <summary>reference op = setup.operations.add(op_input) — None when Fusion rejects the strategy.</summary>
        public CamOperation? Add(CamOperationInput input)
            => throw new System.NotSupportedException("adsknet stub");
    }

    /// <summary>adsk.cam.CamParameter (reference param.name / param.expression — expression is the only setter).</summary>
    public sealed class CamParameter
    {
        public string Name => throw new System.NotSupportedException("adsknet stub");
        public string? Expression { get => throw new System.NotSupportedException("adsknet stub"); set => throw new System.NotSupportedException("adsknet stub"); }
    }

    /// <summary>adsk.cam.CamParameterCollection (reference params.itemByName(name); iterable of name/expression).</summary>
    public sealed class CamParameterCollection : System.Collections.Generic.IEnumerable<CamParameter>
    {
        /// <summary>reference params.itemByName(param_name) — null when absent.</summary>
        public CamParameter? ItemByName(string name) => throw new System.NotSupportedException("adsknet stub");

        public int Count => throw new System.NotSupportedException("adsknet stub");
        public CamParameter Item(int index) => throw new System.NotSupportedException("adsknet stub");

        public System.Collections.Generic.IEnumerator<CamParameter> GetEnumerator()
            => throw new System.NotSupportedException("adsknet stub");

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            => throw new System.NotSupportedException("adsknet stub");
    }

    /// <summary>adsk.cam.PostProcessInput (reference PostProcessInput.create(setup_name, post_config, output_folder, units)).</summary>
    public sealed class PostProcessInput
    {
        public static PostProcessInput Create(string setupName, string postConfigPath, string outputFolder, PostOutputUnitOptions units)
            => throw new System.NotSupportedException("adsknet stub");

        /// <summary>reference post_input.isOpenInEditor = False.</summary>
        public bool IsOpenInEditor { get => throw new System.NotSupportedException("adsknet stub"); set => throw new System.NotSupportedException("adsknet stub"); }
    }
}
