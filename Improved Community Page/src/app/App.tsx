import { useState } from "react";
import {
  Search,
  MoreVertical,
  Monitor,
  Folder,
  Upload,
  Users,
  BookOpen,
  LayoutGrid,
  ChevronRight,
  Share2,
  Star,
  Clock,
  FolderOpen,
  ThumbsUp,
  ThumbsDown,
  TrendingUp,
  BarChart2,
} from "lucide-react";

type Vote = "like" | "dislike" | null;

interface FolderItem {
  id: number;
  name: string;
  author: string;
  sources: number;
  icon: string;
  color: string;
  bg: string;
  likes: number;
  dislikes: number;
}

const initialCommunityFolders: FolderItem[] = [
  { id: 1, name: "Software Requirement", author: "Kiyoshi Nanami", sources: 5, icon: "monitor", color: "#7c6fcd", bg: "#ede9fc", likes: 142, dislikes: 8 },
  { id: 2, name: "ABC", author: "Kiyoshi Nanami", sources: 1, icon: "folder", color: "#c0392b", bg: "#fdecea", likes: 37, dislikes: 12 },
  { id: 3, name: "Machine Learning Basics", author: "Aiko Tanaka", sources: 12, icon: "monitor", color: "#2980b9", bg: "#e8f4fb", likes: 289, dislikes: 5 },
  { id: 4, name: "React Fundamentals", author: "Sam Chen", sources: 8, icon: "folder", color: "#27ae60", bg: "#e9f7ef", likes: 201, dislikes: 14 },
  { id: 5, name: "Data Structures", author: "Priya Sharma", sources: 15, icon: "monitor", color: "#e67e22", bg: "#fef5ec", likes: 174, dislikes: 22 },
];

const personalFolders: FolderItem[] = [
  { id: 6, name: "My Study Notes", author: "Kiyoshi Nanami", sources: 3, icon: "folder", color: "#8e44ad", bg: "#f5eef8", likes: 54, dislikes: 3 },
  { id: 7, name: "Project Research", author: "Kiyoshi Nanami", sources: 7, icon: "monitor", color: "#16a085", bg: "#e8f8f5", likes: 98, dislikes: 7 },
  { id: 8, name: "Shared with Team", author: "Kiyoshi Nanami", sources: 4, icon: "folder", color: "#c0392b", bg: "#fdecea", likes: 31, dislikes: 2 },
];

type Tab = "community" | "personal";

const navItems = [
  { label: "Library", icon: BookOpen },
  { label: "Community", icon: Users },
  { label: "Workspace", icon: LayoutGrid },
  { label: "Upload", icon: Upload },
];

function Avatar({ name, size = "sm" }: { name: string; size?: "sm" | "md" }) {
  const initials = name.split(" ").map((n) => n[0]).join("").slice(0, 2).toUpperCase();
  const sizeClass = size === "sm" ? "w-6 h-6 text-xs" : "w-8 h-8 text-sm";
  return (
    <div className={`${sizeClass} rounded-full bg-violet-600 text-white flex items-center justify-center font-semibold shrink-0`}>
      {initials}
    </div>
  );
}

function formatCount(n: number) {
  return n >= 1000 ? `${(n / 1000).toFixed(1)}k` : String(n);
}

function approvalRate(likes: number, dislikes: number) {
  const total = likes + dislikes;
  return total === 0 ? 0 : Math.round((likes / total) * 100);
}

// ── Community card with like/dislike ──────────────────────────────────────────
function CommunityCard({
  folder,
  vote,
  onVote,
}: {
  folder: FolderItem;
  vote: Vote;
  onVote: (id: number, v: Vote) => void;
}) {
  const [menuOpen, setMenuOpen] = useState(false);
  const IconComp = folder.icon === "monitor" ? Monitor : Folder;

  const displayLikes = folder.likes + (vote === "like" ? 1 : 0);
  const displayDislikes = folder.dislikes + (vote === "dislike" ? 1 : 0);

  function handleVote(v: "like" | "dislike") {
    onVote(folder.id, vote === v ? null : v);
  }

  return (
    <div className="bg-white border border-gray-100 rounded-2xl p-5 hover:shadow-md transition-shadow duration-200 cursor-pointer group relative flex flex-col gap-4">
      <div className="flex items-start justify-between">
        <div className="w-14 h-14 rounded-xl flex items-center justify-center" style={{ backgroundColor: folder.bg }}>
          <IconComp size={26} style={{ color: folder.color }} />
        </div>
        <button
          onClick={() => setMenuOpen(!menuOpen)}
          className="opacity-0 group-hover:opacity-100 transition-opacity p-1.5 rounded-lg hover:bg-gray-100 text-gray-400"
        >
          <MoreVertical size={16} />
        </button>
        {menuOpen && (
          <div className="absolute right-4 top-14 z-10 bg-white border border-gray-100 rounded-xl shadow-lg py-1.5 w-40">
            {["Share", "Report"].map((item) => (
              <button key={item} className="w-full text-left px-4 py-2 text-sm text-gray-700 hover:bg-gray-50" onClick={() => setMenuOpen(false)}>
                {item}
              </button>
            ))}
          </div>
        )}
      </div>

      <div className="space-y-2 flex-1">
        <h3 className="font-semibold text-gray-900 text-sm leading-snug">{folder.name}</h3>
        <div className="flex items-center gap-2">
          <Avatar name={folder.author} />
          <span className="text-xs text-gray-500">{folder.author}</span>
          <span className="ml-auto text-xs text-gray-400">{folder.sources} sources</span>
        </div>
      </div>

      {/* Like / Dislike row */}
      <div className="flex items-center gap-2 pt-3 border-t border-gray-100">
        <button
          onClick={() => handleVote("like")}
          className={`flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-medium transition-all ${
            vote === "like"
              ? "bg-violet-100 text-violet-700"
              : "text-gray-400 hover:bg-gray-100 hover:text-gray-700"
          }`}
        >
          <ThumbsUp size={13} className={vote === "like" ? "fill-violet-600 text-violet-600" : ""} />
          {formatCount(displayLikes)}
        </button>
        <button
          onClick={() => handleVote("dislike")}
          className={`flex items-center gap-1.5 px-3 py-1.5 rounded-lg text-xs font-medium transition-all ${
            vote === "dislike"
              ? "bg-red-100 text-red-600"
              : "text-gray-400 hover:bg-gray-100 hover:text-gray-700"
          }`}
        >
          <ThumbsDown size={13} className={vote === "dislike" ? "fill-red-500 text-red-500" : ""} />
          {formatCount(displayDislikes)}
        </button>
        {/* approval bar */}
        <div className="flex-1 flex items-center gap-1.5 ml-1">
          <div className="h-1.5 flex-1 rounded-full bg-gray-100 overflow-hidden">
            <div
              className="h-full rounded-full bg-violet-500 transition-all duration-300"
              style={{ width: `${approvalRate(displayLikes, displayDislikes)}%` }}
            />
          </div>
          <span className="text-[10px] text-gray-400 shrink-0">{approvalRate(displayLikes, displayDislikes)}%</span>
        </div>
      </div>
    </div>
  );
}

// ── Personal dashboard card ────────────────────────────────────────────────────
function PersonalCard({ folder }: { folder: FolderItem }) {
  const IconComp = folder.icon === "monitor" ? Monitor : Folder;
  const rate = approvalRate(folder.likes, folder.dislikes);
  const total = folder.likes + folder.dislikes;

  return (
    <div className="bg-white border border-gray-100 rounded-2xl overflow-hidden hover:shadow-md transition-shadow duration-200">
      {/* top accent strip */}
      <div className="h-1.5" style={{ backgroundColor: folder.color }} />
      <div className="p-5 space-y-4">
        {/* header */}
        <div className="flex items-center gap-3">
          <div className="w-11 h-11 rounded-xl flex items-center justify-center shrink-0" style={{ backgroundColor: folder.bg }}>
            <IconComp size={20} style={{ color: folder.color }} />
          </div>
          <div className="flex-1 min-w-0">
            <h3 className="font-semibold text-gray-900 text-sm truncate">{folder.name}</h3>
            <p className="text-xs text-gray-400">{folder.sources} sources</p>
          </div>
          <span
            className="text-xs font-bold px-2 py-0.5 rounded-full"
            style={{ backgroundColor: folder.bg, color: folder.color }}
          >
            {rate}%
          </span>
        </div>

        {/* approval bar */}
        <div className="space-y-1">
          <div className="flex justify-between text-[10px] text-gray-400">
            <span>Approval rate</span>
            <span>{total} votes</span>
          </div>
          <div className="h-2 rounded-full bg-gray-100 overflow-hidden flex">
            <div
              className="h-full rounded-l-full transition-all duration-500"
              style={{ width: `${rate}%`, backgroundColor: folder.color }}
            />
            <div className="h-full flex-1 bg-red-100" />
          </div>
        </div>

        {/* stat pills */}
        <div className="grid grid-cols-2 gap-2">
          <div className="flex items-center gap-2 bg-violet-50 rounded-xl px-3 py-2.5">
            <ThumbsUp size={14} className="text-violet-600 fill-violet-100" />
            <div>
              <p className="text-base font-bold text-violet-700 leading-none">{formatCount(folder.likes)}</p>
              <p className="text-[10px] text-violet-400 mt-0.5">Likes</p>
            </div>
          </div>
          <div className="flex items-center gap-2 bg-red-50 rounded-xl px-3 py-2.5">
            <ThumbsDown size={14} className="text-red-500 fill-red-100" />
            <div>
              <p className="text-base font-bold text-red-600 leading-none">{formatCount(folder.dislikes)}</p>
              <p className="text-[10px] text-red-400 mt-0.5">Dislikes</p>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

// ── Personal summary bar ───────────────────────────────────────────────────────
function PersonalSummary({ folders }: { folders: FolderItem[] }) {
  const totalLikes = folders.reduce((s, f) => s + f.likes, 0);
  const totalDislikes = folders.reduce((s, f) => s + f.dislikes, 0);
  const overall = approvalRate(totalLikes, totalDislikes);
  const best = [...folders].sort((a, b) => b.likes - a.likes)[0];

  return (
    <div className="grid grid-cols-1 sm:grid-cols-3 gap-4 mb-8">
      {[
        {
          label: "Total Likes",
          value: formatCount(totalLikes),
          icon: ThumbsUp,
          bg: "bg-violet-50",
          text: "text-violet-700",
          sub: "text-violet-400",
          iconFill: "fill-violet-200 text-violet-600",
        },
        {
          label: "Total Dislikes",
          value: formatCount(totalDislikes),
          icon: ThumbsDown,
          bg: "bg-red-50",
          text: "text-red-700",
          sub: "text-red-400",
          iconFill: "fill-red-200 text-red-500",
        },
        {
          label: "Overall Approval",
          value: `${overall}%`,
          icon: TrendingUp,
          bg: "bg-emerald-50",
          text: "text-emerald-700",
          sub: "text-emerald-400",
          iconFill: "text-emerald-500",
        },
      ].map(({ label, value, icon: Icon, bg, text, sub, iconFill }) => (
        <div key={label} className={`${bg} rounded-2xl px-5 py-4 flex items-center gap-4`}>
          <div className="w-10 h-10 rounded-xl bg-white flex items-center justify-center shadow-sm">
            <Icon size={18} className={iconFill} />
          </div>
          <div>
            <p className={`text-2xl font-bold ${text} leading-none`}>{value}</p>
            <p className={`text-xs ${sub} mt-1`}>{label}</p>
          </div>
        </div>
      ))}
      {best && (
        <div className="sm:col-span-3 bg-white border border-gray-100 rounded-2xl px-5 py-3 flex items-center gap-3">
          <BarChart2 size={16} className="text-violet-400 shrink-0" />
          <p className="text-xs text-gray-500">
            Top performing folder:{" "}
            <span className="font-semibold text-gray-800">{best.name}</span>
            {" "}with{" "}
            <span className="font-semibold text-violet-700">{formatCount(best.likes)} likes</span>
          </p>
        </div>
      )}
    </div>
  );
}

// ── Root ───────────────────────────────────────────────────────────────────────
export default function App() {
  const [activeTab, setActiveTab] = useState<Tab>("community");
  const [activeNav, setActiveNav] = useState("Community");
  const [searchQuery, setSearchQuery] = useState("");
  const [communityFolders, setCommunityFolders] = useState(initialCommunityFolders);
  const [votes, setVotes] = useState<Record<number, Vote>>({});

  function handleVote(id: number, v: Vote) {
    setVotes((prev) => ({ ...prev, [id]: v }));
  }

  const folders = activeTab === "community" ? communityFolders : personalFolders;
  const filtered = folders.filter((f) =>
    f.name.toLowerCase().includes(searchQuery.toLowerCase())
  );

  return (
    <div className="min-h-screen bg-gray-50 flex flex-col">
      {/* Top nav */}
      <header className="bg-white border-b border-gray-100 px-6 flex items-center justify-between h-14 shrink-0">
        <div className="flex items-center gap-8">
          <div className="flex items-center gap-2.5">
            <div className="w-8 h-8 rounded-lg bg-violet-600 flex items-center justify-center">
              <BookOpen size={16} className="text-white" />
            </div>
            <span className="font-semibold text-gray-900 text-sm">AI Study Hub</span>
          </div>
          <nav className="flex items-center gap-1">
            {navItems.map((item) => {
              const isActive = activeNav === item.label;
              return (
                <button
                  key={item.label}
                  onClick={() => setActiveNav(item.label)}
                  className={`px-4 py-4 text-sm font-medium border-b-2 transition-colors ${
                    isActive ? "border-violet-600 text-violet-700" : "border-transparent text-gray-500 hover:text-gray-800"
                  }`}
                >
                  {item.label}
                </button>
              );
            })}
          </nav>
        </div>
        <div className="flex items-center gap-3">
          <Avatar name="Kiyoshi Nanami" size="md" />
          <div className="hidden sm:block text-right">
            <p className="text-xs font-semibold text-gray-900 leading-none">Kiyoshi Nanami</p>
            <p className="text-[10px] text-violet-500 font-medium mt-0.5">PRO · PLAN</p>
          </div>
          <button className="p-2 rounded-lg hover:bg-gray-100 text-gray-400">
            <Share2 size={16} />
          </button>
        </div>
      </header>

      <div className="flex flex-1 overflow-hidden">
        {/* Left panel */}
        <aside className="w-64 bg-white border-r border-gray-100 flex flex-col shrink-0">
          {/* Toggle tabs */}
          <div className="p-4 border-b border-gray-100">
            <div className="bg-gray-100 rounded-xl p-1 flex gap-1">
              <button
                onClick={() => { setActiveTab("community"); setSearchQuery(""); }}
                className={`flex-1 flex items-center justify-center gap-1.5 py-2 px-2 rounded-lg text-xs font-semibold transition-all duration-200 ${
                  activeTab === "community" ? "bg-white text-violet-700 shadow-sm" : "text-gray-500 hover:text-gray-700"
                }`}
              >
                <Users size={13} />
                Community
              </button>
              <button
                onClick={() => { setActiveTab("personal"); setSearchQuery(""); }}
                className={`flex-1 flex items-center justify-center gap-1.5 py-2 px-2 rounded-lg text-xs font-semibold transition-all duration-200 ${
                  activeTab === "personal" ? "bg-white text-violet-700 shadow-sm" : "text-gray-500 hover:text-gray-700"
                }`}
              >
                <Share2 size={13} />
                Personal Shared
              </button>
            </div>
          </div>

          {/* Folder list */}
          <div className="flex-1 overflow-y-auto p-3">
            <p className="text-[10px] font-semibold text-gray-400 uppercase tracking-widest px-2 mb-2">
              {activeTab === "community" ? "Public Folders" : "My Shared Folders"}
            </p>
            <div className="space-y-0.5">
              {folders.map((folder) => {
                const IconComp = folder.icon === "monitor" ? Monitor : Folder;
                return (
                  <button key={folder.id} className="w-full flex items-center gap-3 px-2.5 py-2 rounded-lg text-left hover:bg-gray-50 group transition-colors">
                    <div className="w-7 h-7 rounded-lg flex items-center justify-center shrink-0" style={{ backgroundColor: folder.bg }}>
                      <IconComp size={14} style={{ color: folder.color }} />
                    </div>
                    <div className="flex-1 min-w-0">
                      <p className="text-xs font-medium text-gray-700 truncate">{folder.name}</p>
                      {activeTab === "personal" ? (
                        <div className="flex items-center gap-2 mt-0.5">
                          <ThumbsUp size={9} className="text-violet-400" />
                          <span className="text-[10px] text-gray-400">{folder.likes}</span>
                          <ThumbsDown size={9} className="text-red-400" />
                          <span className="text-[10px] text-gray-400">{folder.dislikes}</span>
                        </div>
                      ) : (
                        <p className="text-[10px] text-gray-400">{folder.sources} sources</p>
                      )}
                    </div>
                    <ChevronRight size={12} className="text-gray-300 opacity-0 group-hover:opacity-100 shrink-0" />
                  </button>
                );
              })}
            </div>
          </div>

          {/* Footer */}
          <div className="border-t border-gray-100 p-3 space-y-0.5">
            {[
              { icon: Star, label: "Starred" },
              { icon: Clock, label: "Recent" },
              { icon: FolderOpen, label: "All Folders" },
            ].map(({ icon: Icon, label }) => (
              <button key={label} className="w-full flex items-center gap-3 px-2.5 py-2 rounded-lg text-left hover:bg-gray-50 text-gray-500 hover:text-gray-700 transition-colors">
                <Icon size={15} />
                <span className="text-xs font-medium">{label}</span>
              </button>
            ))}
          </div>
        </aside>

        {/* Main */}
        <main className="flex-1 overflow-y-auto p-8">
          <div className="max-w-5xl">
            <div className="mb-6">
              <h1 className="text-2xl font-semibold text-gray-900">
                {activeTab === "community" ? "Public Community" : "Personal Shared"}
              </h1>
              <p className="text-sm text-gray-400 mt-1">
                {activeTab === "community"
                  ? "Browse and rate folders shared by the community"
                  : "Engagement dashboard for your shared folders"}
              </p>
            </div>

            {/* Personal summary stats */}
            {activeTab === "personal" && <PersonalSummary folders={personalFolders} />}

            {/* Search */}
            <div className="relative mb-6 max-w-sm">
              <Search size={15} className="absolute left-3.5 top-1/2 -translate-y-1/2 text-gray-400" />
              <input
                type="text"
                placeholder="Search folders..."
                value={searchQuery}
                onChange={(e) => setSearchQuery(e.target.value)}
                className="w-full pl-10 pr-4 py-2.5 text-sm bg-white border border-gray-200 rounded-xl focus:outline-none focus:ring-2 focus:ring-violet-200 focus:border-violet-400 transition-all placeholder:text-gray-400"
              />
            </div>

            {/* Grid */}
            {filtered.length > 0 ? (
              <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-4">
                {activeTab === "community"
                  ? filtered.map((folder) => (
                      <CommunityCard
                        key={folder.id}
                        folder={folder}
                        vote={votes[folder.id] ?? null}
                        onVote={handleVote}
                      />
                    ))
                  : filtered.map((folder) => (
                      <PersonalCard key={folder.id} folder={folder} />
                    ))}
              </div>
            ) : (
              <div className="flex flex-col items-center justify-center py-24 text-center">
                <FolderOpen size={40} className="text-gray-200 mb-4" />
                <p className="text-sm font-medium text-gray-400">No folders found</p>
                <p className="text-xs text-gray-300 mt-1">Try a different search term</p>
              </div>
            )}
          </div>
        </main>
      </div>
    </div>
  );
}
