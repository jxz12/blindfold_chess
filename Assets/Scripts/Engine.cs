﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

////////////////////////////////////////////////////////
// a class to save all the information of a chess game

public partial class Engine
{
    private enum Piece { None=0, Pawn, Rook, Knight, Bishop, Queen, King,
                         VirginPawn, VirginRook, VirginKing }; // for castling, en passant etc.
    private Piece[] whitePieces;
    private Piece[] blackPieces;
    private Dictionary<int, HashSet<int>> castles; // king->rook

    // a class to store all the information needed for a move
    // a Move plus the board state is all the info needed for move generation
    private class Move
    {
        public enum Special { Null, Normal, Castle, Puush, EnPassant }; // FIXME: Null move is also used for pawns and castling

        public Move previous = null;
        public bool whiteMove = false;
        public int source = 0;
        public int target = 0;
        public Special type = Special.Null;
        public Piece moved = Piece.None;
        public Piece captured = Piece.None;
        public Piece promotion = Piece.None;
        public int halfMoveClock = 0; // FIXME: check for draw after 50 moves
    }

    // board dimensions
    public int NRanks { get; private set; }
    public int NFiles { get; private set; }
    
    // current to evaluate
    private Move prevMove;
    private int totalPly;

    // Chess is ugly, here are some examples:
    //   is castling only on the home rank?
    //   where the heck does the king go when castling? (here I had to give 2 options for chess960)
    //   double pawn push is only from home rank +- 1? 
    //   do not get me started on en passant...
    private bool castle960;

    public Engine(string FEN="rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w AHah - 0 1", bool castle960=false)
    {
        // board = new Board();
        NRanks = FEN.Count(c=>c=='/') + 1;
        NFiles = 0;
        foreach (char c in FEN)
        {
            if (c == '/') {
                break;
            } else if (c >= '1' && c <= '9') {
                NFiles += c - '0';
            } else {
                NFiles += 1;
            }
        }
        if (NFiles > 23 || NRanks > 12) {
            throw new Exception("cannot have more than 23x12 board (blame ASCII lol)");
        }
        whitePieces = new Piece[NRanks*NFiles];
        blackPieces = new Piece[NRanks*NFiles];

        int rank = NRanks-1;
        int file = -1;
        int i = 0;
        while (FEN[i] != ' ')
        {
            file += 1;
            int pos = GetPos(rank, file);
            if (FEN[i] == '/')
            {
                if (file != NFiles) {
                    throw new Exception("wrong number of squares in FEN rank " + rank);
                }
                rank -= 1;
                file = -1;
            }
            else if (FEN[i] >= '1' && FEN[i] <= '9')
            {
                file += FEN[i] - '1'; // -1 because file will be incremented regardless
            }
            else if (FEN[i] == 'P') { whitePieces[pos] = Piece.VirginPawn; }
            else if (FEN[i] == 'R') { whitePieces[pos] = Piece.Rook; }
            else if (FEN[i] == 'N') { whitePieces[pos] = Piece.Knight; }
            else if (FEN[i] == 'B') { whitePieces[pos] = Piece.Bishop; }
            else if (FEN[i] == 'Q') { whitePieces[pos] = Piece.Queen; }
            else if (FEN[i] == 'K') { whitePieces[pos] = Piece.VirginKing; }

            else if (FEN[i] == 'p') { blackPieces[pos] = Piece.VirginPawn; }
            else if (FEN[i] == 'r') { blackPieces[pos] = Piece.Rook; }
            else if (FEN[i] == 'n') { blackPieces[pos] = Piece.Knight; }
            else if (FEN[i] == 'b') { blackPieces[pos] = Piece.Bishop; }
            else if (FEN[i] == 'q') { blackPieces[pos] = Piece.Queen; }
            else if (FEN[i] == 'k') { blackPieces[pos] = Piece.VirginKing; }
            else { throw new Exception("unexpected character " + FEN[i] + " at " + i); }

            i += 1;
        }

        // who to move
        prevMove = new Move();
        i += 1;
        if (FEN[i] == 'w') { prevMove.whiteMove = false; }
        else if (FEN[i] == 'b') { prevMove.whiteMove = true; }
        else { throw new Exception("unexpected character " + FEN[i] + " at " + i); }

        // castling I HATE YOU
        i += 2;
        castles = new Dictionary<int, HashSet<int>>();
        for (int pos=0; pos<whitePieces.Length; pos++) // init hashsets
        {
            if (whitePieces[pos] == Piece.VirginKing || blackPieces[pos] == Piece.VirginKing) {
                castles[pos] = new HashSet<int>();
            }
        }
        while (FEN[i] != ' ') // make rooks virgins
        {
            if ((FEN[i] >= 'A' && FEN[i] <= 'Z') || (FEN[i] >= 'a' && FEN[i] <= 'z'))
            {
                bool white = char.IsUpper(FEN[i]);
                int rookFile = white? FEN[i] - 'A' : FEN[i] - 'a';
                int rookRank = white? 0 : NRanks-1;
                var allies = white? whitePieces : blackPieces;

                int rookPos = GetPos(rookRank, rookFile);
                if (allies[rookPos] != Piece.Rook)
                {
                    throw new Exception("no rook on " + FEN[i] + " file");
                }
                // find closest King left
                for (int kingFile=rookFile-1; kingFile>=0; kingFile--)
                {
                    int kingPos = GetPos(rookRank, kingFile);
                    if (allies[kingPos] == Piece.VirginKing)
                    {
                        allies[rookPos] = Piece.VirginRook;
                        castles[kingPos].Add(rookPos);
                        break; // only connect to closest king
                    }
                }
                // find closest King right
                for (int kingFile=rookFile+1; kingFile<NFiles; kingFile++)
                {
                    int kingPos = GetPos(rookRank, kingFile);
                    if (allies[kingPos] == Piece.VirginKing)
                    {
                        allies[rookPos] = Piece.VirginRook;
                        castles[kingPos].Add(rookPos);
                        break; // only connect to closest king
                    }
                }
            }
            else if (FEN[i] == '-') {}
            else { throw new Exception("unexpected character " + FEN[i] + " at " + i); }

            i += 1;
        }

        // en passant
        i += 1;
        if (FEN[i] != '-')
        {
            file = FEN[i] - 'a';
            rank = FEN[i] - '1';

            if (file < 0 || file >= NFiles || rank < 0 || rank >= NRanks) {
                throw new Exception("unexpected character " + FEN[i] + " at " + i);
            } else {
                prevMove.moved = Piece.VirginPawn;
                prevMove.source = prevMove.whiteMove? GetPos(1, file) : GetPos(NRanks-2, file);
                prevMove.target = GetPos(rank, file);
            }
            i += 1;
        }

        // half move clock
        i += 2;
        int len = 1;
        while (FEN[i+len] != ' ')
        {
            len += 1;
        }
        prevMove.halfMoveClock = int.Parse(FEN.Substring(i, len));

        i += len + 1;
        totalPly = 2*int.Parse(FEN.Substring(i)) - (prevMove.whiteMove? 1:0);

        this.castle960 = castle960;

        legalMoves = FindLegalMoves(prevMove);
    }

    public int GetRank(int pos) {
        return pos / NFiles;
    }
    public int GetFile(int pos) {
        return pos % NFiles;
    }
    public int GetPos(int rank, int file) {
        return rank * NFiles + file;
    }
    public bool InBounds(int rank, int file) {
        return file>=0 && file<NFiles && rank>=0 && rank<NRanks;
    }
    public bool Occupied(int pos) {
        // return whitePieces.ContainsKey(pos) || blackPieces.ContainsKey(pos);
        return whitePieces[pos] != Piece.None || blackPieces[pos] != Piece.None;
    }


    ///////////////////////////////////
    // for interface from the outside

    private Dictionary<string, Move> legalMoves;
    public void PlayPGN(string algebraic)
    {
        Move toPlay;
        if (legalMoves.TryGetValue(algebraic, out toPlay))
        {
            PlayMove(toPlay);
            prevMove = toPlay;
            legalMoves = FindLegalMoves(prevMove);
            totalPly += 1;
        }
        else
        {
            throw new Exception("move not legal");
        }
    }
    public IEnumerable<string> GetPGNs()
    {
        return legalMoves.Keys;
    }
    public string GetLastUCI()
    {
        var sb = new StringBuilder();
        sb.Append((char)('a'+GetFile(prevMove.source)));
        sb.Append(GetRank(prevMove.source));
        sb.Append((char)('a'+GetFile(prevMove.target)));
        sb.Append(GetRank(prevMove.target));

        // // fuck chess
        // if (prevMove.type == Move.Special.Castle)
        //     sb.Append(fuck castling);
        // if (prevMove.type == Move.Special.EnPassant)
        //     sb.Append("x").Append();
        if (prevMove.promotion != Piece.None) {
            sb.Append(pieceStrings[prevMove.promotion]);
        }
        return sb.ToString();
    }
    public void UndoLastMove()
    {
        if (prevMove != null)
        {
            UndoMove(prevMove);
            prevMove = prevMove.previous;
            legalMoves = FindLegalMoves(prevMove);
            totalPly -= 1;
        }
        else
        {
            throw new Exception("no moves played yet");
        }
    }

    private static Dictionary<Piece, string> pieceStrings = new Dictionary<Piece, string>() {
        { Piece.Pawn, "P" },
        { Piece.VirginPawn, "P" },
        { Piece.Rook, "R" },
        { Piece.VirginRook, "R" },
        { Piece.Knight, "N" },
        { Piece.Bishop, "B" },
        { Piece.Queen, "Q" },
        { Piece.King, "K" },
        { Piece.VirginKing, "K" },
    };

    public string ToFEN()
    {
        var sb = new StringBuilder();

        int empty = 0;
        int rank = NRanks-1;
        int file = 0;
        while (rank >= 0)
        {
            int pos = GetPos(rank, file);
            if (whitePieces[pos] == Piece.None && blackPieces[pos] == Piece.None)
            {
                empty += 1;
                if (empty >= 8)
                {
                    sb.Append(empty);
                    empty = 0;
                }
            }
            else
            {
                if (empty > 0)
                {
                    sb.Append(empty);
                    empty = 0;
                }
                if (whitePieces[pos] != Piece.None && blackPieces[pos] != Piece.None)
                {
                    throw new Exception("white and black on same square boo");
                }
                else if (whitePieces[pos] != Piece.None)
                {
                    sb.Append(pieceStrings[whitePieces[pos]]);
                }
                else // if (blackPieces[pos] != Piece.None)
                {
                    sb.Append(pieceStrings[blackPieces[pos]].ToLower());
                }
            }
            file += 1;
            if (file >= NFiles)
            {
                file = 0;
                rank -= 1;
                if (empty > 0)
                {
                    sb.Append(empty);
                    empty = 0;
                }
                if (rank >= 0) {
                    sb.Append('/');
                }
            }
        }

        // who to move
        sb.Append(' ').Append(totalPly%2==0? 'w' : 'b').Append(' ');

        // castling
        var shredderCastles = new HashSet<char>();
        for (int pos=0; pos<whitePieces.Length; pos++)
        {
            if (whitePieces[pos] == Piece.VirginKing)
            {
                foreach (var rook in castles[pos])
                {
                    if (whitePieces[rook] == Piece.VirginRook)
                    {
                        shredderCastles.Add((char)('A'+GetFile(rook)));
                    }
                }
            }
        }
        foreach (char rookFile in shredderCastles.OrderBy(x=>x))
        {
            sb.Append(rookFile);
        }
        shredderCastles.Clear();
        for (int pos=0; pos<blackPieces.Length; pos++)
        {
            if (blackPieces[pos] == Piece.VirginKing)
            {
                foreach (var rook in castles[pos])
                {
                    if (blackPieces[rook] == Piece.VirginRook)
                    {
                        shredderCastles.Add((char)('a'+GetFile(rook)));
                    }
                }
            }
        }
        foreach (char rookFile in shredderCastles.OrderBy(x=>x))
        {
            sb.Append(rookFile);
        }

        // en passant
        if (prevMove.type == Move.Special.Puush) {
            sb.Append(' ')
              .Append((char)('a' + GetFile(prevMove.target)))
              .Append((char)('1' + GetRank(prevMove.target)));
        } else {
            sb.Append(' ').Append("-");
        }

        // half move clock
        sb.Append(' ').Append(prevMove.halfMoveClock);

        // full move clock
        sb.Append(' ').Append((totalPly + (prevMove.whiteMove? 1:0)) / 2);
        // totalPly = 2*int.Parse(FEN.Substring(i)) - (prevMove.whiteMove? 1:0);

        return sb.ToString();
    }

    private string ToPGN(Move move)
    {
        var sb = new StringBuilder();
        if (move.moved == Piece.Pawn || move.moved == Piece.VirginPawn)
        {
            sb.Append((char)('a'+(move.source%NFiles)));
            if (move.captured != Piece.None)
            {
                sb.Append('x').Append((char)('a'+(move.target%NFiles)));
            }
            sb.Append((char)('1'+(move.target/NFiles)));
            if (move.promotion != Piece.None
                && move.promotion != Piece.Pawn)
            {
                sb.Append('=');
                sb.Append(pieceStrings[move.promotion]);
            }
        }
        else
        {
            sb.Append(pieceStrings[move.moved]);
            if (move.captured != Piece.None) {
                sb.Append('x');
            }
            sb.Append((char)('a'+(move.target%NFiles)));
            sb.Append((char)('1'+(move.target/NFiles)));
        }
        return sb.ToString();
    }
    private Dictionary<string, Move> FindLegalMoves(Move prev)
    {
        var nexts = new List<Move>(FindPseudoLegalMoves(prev));
        var ambiguous = new Dictionary<string, List<Move>>();

        foreach (Move next in nexts)
        {
            if (next.type == Move.Special.Null) {
                continue;
            }
            PlayMove(next);
            var nextnexts = FindPseudoLegalMoves(next);

            // if legal, add to list
            if (!InCheck(next, nextnexts))
            {
                string algebraic = ToPGN(next);
                if (ambiguous.ContainsKey(algebraic))
                {
                    ambiguous[algebraic].Add(next);
                }
                else
                {
                    ambiguous[algebraic] = new List<Move> { next };
                }
            }
            UndoMove(next);
        }
        var unambiguous = new Dictionary<string, Move>();
        foreach (string algebraic in ambiguous.Keys)
        {
            if (ambiguous[algebraic].Count == 1) // already unambiguous
            {
                unambiguous[algebraic] = ambiguous[algebraic][0];
            }
            else
            {
                // ambiguous
                foreach (Move move in ambiguous[algebraic])
                {
                    // check which coordinates (file/rank) clash
                    int file = GetFile(move.source);
                    int rank = GetRank(move.source);
                    bool repeatFile = false;
                    bool repeatRank = false;
                    foreach (Move clash in ambiguous[algebraic].Where(x=> x!=move))
                    {
                        repeatFile |= file == GetFile(clash.source);
                        repeatRank |= rank == GetRank(clash.source);
                    }
                    // if no shared file, use file
                    string disambiguated;
                    if (!repeatFile)
                    {
                        disambiguated = algebraic.Insert(1, ((char)('a'+file)).ToString());
                    }
                    else if (!repeatRank) // use rank
                    {
                        disambiguated = algebraic.Insert(1, ((char)('1'+rank)).ToString());
                    }
                    else // use both
                    {
                        disambiguated = algebraic.Insert(1, ((char)('a'+file)).ToString()
                                                            + ((char)('1'+rank)).ToString());
                    }
                    unambiguous[disambiguated] = move;
                }
            }
        }
        return unambiguous;
    }
}
