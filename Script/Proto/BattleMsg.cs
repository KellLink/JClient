using System;

public class BattleMsg
{
    public class MsgMove : MsgBase
    {
        public int x;
        public int y;
        public int z;

        public MsgMove()
        {
            protoName = "MsgMove";
        }
    }

    public class MsgAttack : MsgBase
    {
        public String desc = "127.0.0.1:8888";

        public MsgAttack()
        {
            protoName = "MsgAttack";
        }
    }
}