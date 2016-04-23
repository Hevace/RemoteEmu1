using System.Collections.Generic;

namespace RemoteEmu1
{
    public struct CmdReturnData
    {
        enum CmdReturnStatus { Ok = 0, Info, Warning, Error };
        CmdReturnStatus Status;     // Error is an unsuccessful command
        string Msg;                 // message output by the command. May be an error message.
        object Retval;              // value returned by the command
    }

    public struct CmdType
    {
        string Name;                // The command name
        delegate CmdReturnData Cmd(object[] param);
    }

    public struct ScriptableValue<T>
    {

    }

    interface IScriptableObject
    {
        string ScriptableName { get; }              // name of this scriptable object

        double this[string itemname] { get; set; }  // get or set a data item in the object as a double

        CmdReturnData Exec(string cmdname, List<object> param);          // run the scriptable object command with parameters
    }
}
